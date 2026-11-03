using System.Collections.Concurrent;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Tokens;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class PostgresConcurrentMaskTests
{
    [Fact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ConcurrentMasks_CreateOneJobAndDeleteRejectedUpload()
    {
        var connection = Environment.GetEnvironmentVariable(
            "UPSCALELAB_TOKEN_TEST_CONNECTION");

        Assert.False(string.IsNullOrWhiteSpace(connection));

        var cs = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        Assert.Equal("upscalelab_token_test", cs.Database);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection)
            .Options;

        var userId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        await using (var setup = new ApplicationDbContext(options))
        {
            Assert.Equal(
                "upscalelab_token_test",
                setup.Database.GetDbConnection().Database);

            setup.Users.Add(new User
            {
                Id = userId,
                Email = $"mask-{userId:N}@example.invalid",
                Username = $"mask-{userId:N}",
                PasswordHash = "test-only"
            });

            setup.Images.Add(new UpscaleLab.Domain.Entities.Image
            {
                Id = imageId,
                UserId = userId,
                OriginalUrl = "https://example.invalid/source.png",
                OriginalObjectKey = "tests/mask/source.png",
                OriginalWidth = 10,
                OriginalHeight = 10,
                FileName = "source.png",
                ContentType = "image/png"
            });

            setup.LiveLayerProjects.Add(new LiveLayerProject
            {
                Id = projectId,
                UserId = userId,
                OriginalImageId = imageId,
                Title = "Concurrent mask test"
            });

            await setup.SaveChangesAsync();
        }

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(45));

        var ct = timeout.Token;
        var store = new ConcurrentMaskStore();
        var queue = new ProcessingJobQueue();

        async Task<(bool Success, Exception? Error)> StartMask()
        {
            try
            {
                await using var db = new ApplicationDbContext(options);

                var service = new ProjectProcessingService(
                    db,
                    store,
                    new StartOnlyProcessor(),
                    queue,
                    NullLogger<ProjectProcessingService>.Instance,
                    new TokenService(db));

                using var mask = CreateMask();

                await service.RefineSubjectAsync(
                    userId,
                    projectId,
                    Guid.Empty,
                    mask,
                    ct);

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex);
            }
        }

        try
        {
            Task<(bool Success, Exception? Error)> first;
            Task<(bool Success, Exception? Error)> second;

            await using (var gate = new ApplicationDbContext(options))
            {
                await using (var gateTx =
                    await gate.Database.BeginTransactionAsync(ct))
                {
                    var locked = await gate.LiveLayerProjects
                        .FromSqlInterpolated($"""
                            SELECT * FROM live_layer_projects
                            WHERE "Id" = {projectId}
                            FOR UPDATE
                            """)
                        .AsNoTracking()
                        .ToListAsync(ct);

                    Assert.Single(locked);

                    first = Task.Run(StartMask, ct);
                    second = Task.Run(StartMask, ct);

                    // 두 마스크 업로드 완료를 기다린다.
                    // DB 잠금은 아직 해제하지 않는다.
                    await store.UploadsReady.WaitAsync(ct);

                    Assert.Equal(2, store.UploadCount);
                    Assert.Equal(2, store.Files.Count);

                    Assert.False(first.IsCompleted);
                    Assert.False(second.IsCompleted);

                    await gateTx.CommitAsync(ct);
                }
            }

            var results = await Task.WhenAll(first, second)
                .WaitAsync(ct);

            Assert.Equal(1, results.Count(x => x.Success));

            var rejected = Assert.Single(
                results, x => !x.Success);

            Assert.IsType<ConflictException>(rejected.Error);

            await using var verify = new ApplicationDbContext(options);

            var job = Assert.Single(
                await verify.ProcessingJobs
                    .AsNoTracking()
                    .Where(x => x.ProjectId == projectId)
                    .ToListAsync(ct));

            Assert.Equal(
                ProcessingJobStatus.Queued,
                job.Status);

            Assert.NotNull(job.SubjectMaskObjectKey);

            var tokenTransaction = Assert.Single(
                await verify.TokenTransactions
                    .AsNoTracking()
                    .Where(x => x.UserId == userId)
                    .ToListAsync(ct));

            Assert.Equal(3, tokenTransaction.Amount);
            Assert.Equal(
                TokenTransactionStatus.Reserved,
                tokenTransaction.Status);

            var wallet = await verify.UserTokenWallets
                .AsNoTracking()
                .SingleAsync(x => x.UserId == userId, ct);

            Assert.Equal(7, wallet.Balance);
            Assert.Equal(0, wallet.PurchasedBalance);

            // 성공한 작업의 마스크만 남아 있어야 한다.
            Assert.Equal(2, store.UploadCount);
            Assert.Single(store.Files);
            Assert.True(
                store.Files.ContainsKey(job.SubjectMaskObjectKey!));
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);

            await cleanup.TokenTransactions
                .Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();

            await cleanup.ProcessingJobs
                .Where(x => x.ProjectId == projectId)
                .ExecuteDeleteAsync();

            await cleanup.LiveLayerProjects
                .Where(x => x.Id == projectId)
                .ExecuteDeleteAsync();

            await cleanup.Images
                .Where(x => x.Id == imageId)
                .ExecuteDeleteAsync();

            await cleanup.UserTokenWallets
                .Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();

            await cleanup.Users
                .Where(x => x.Id == userId)
                .ExecuteDeleteAsync();
        }
    }

    private static MemoryStream CreateMask()
    {
        using var image = new MagickImage(
            MagickColors.Black, 10, 10);

        using var subject = new MagickImage(
            MagickColors.White, 5, 10);

        image.Composite(
            subject, 0, 0, CompositeOperator.Over);

        return new MemoryStream(
            image.ToByteArray(MagickFormat.Png));
    }

    private sealed class StartOnlyProcessor : IProjectImageProcessor
    {
        public bool Supports(ProcessingOperation operation) => true;

        public Task<ProjectProcessingResult> ProcessAsync(
            ProjectProcessingRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "테스트에서는 GPU 작업을 실행하지 않습니다.");
    }

    private sealed class ConcurrentMaskStore : IStorageService
    {
        public ConcurrentDictionary<string, byte[]> Files { get; } = new();

        private readonly TaskCompletionSource uploadsReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int uploadCount;

        public int UploadCount => Volatile.Read(ref uploadCount);

        public Task UploadsReady => uploadsReady.Task;

        public async Task<StoredObject> UploadAsync(
            Stream content,
            string key,
            string contentType,
            CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);

            Files[key] = buffer.ToArray();

            if (Interlocked.Increment(ref uploadCount) == 2)
                uploadsReady.TrySetResult();

            // 두 요청이 업로드를 마친 뒤 DB 잠금에 진입한다.
            await uploadsReady.Task.WaitAsync(ct);

            return new StoredObject(key, "s3://test/" + key);
        }

        public Task DownloadAsync(
            string key,
            Stream destination,
            CancellationToken ct) =>
            destination.WriteAsync(Files[key], ct).AsTask();

        public Task DeleteAsync(
            string key,
            CancellationToken ct)
        {
            Files.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public string CreateUploadUrl(
            string objectKey,
            string contentType,
            TimeSpan lifetime) =>
            throw new NotSupportedException();

        public string CreateDownloadUrl(
            string key,
            TimeSpan lifetime) =>
            "https://storage.test/" + key;
    }
}
