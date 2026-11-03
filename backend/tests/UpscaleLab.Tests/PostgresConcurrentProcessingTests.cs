using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Tokens;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class PostgresConcurrentProcessingTests
{
    [Fact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task ConcurrentStarts_CreateOnlyOneJobAndOneReservation()
    {
        var connection = Environment.GetEnvironmentVariable(
            "UPSCALELAB_TOKEN_TEST_CONNECTION");

        Assert.False(string.IsNullOrWhiteSpace(connection));

        var cs = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        Assert.Equal("upscalelab_token_test", cs.Database);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection)
            .Options;

        await using var setup = new ApplicationDbContext(options);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"concurrent-{Guid.NewGuid():N}@example.invalid",
            Username = $"concurrent-{Guid.NewGuid():N}",
            PasswordHash = "test-only"
        };

        var image = new UpscaleLab.Domain.Entities.Image
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OriginalUrl = "https://example.invalid/source.png",
            OriginalObjectKey = "tests/concurrent/source.png",
            OriginalWidth = 1280,
            OriginalHeight = 720,
            FileName = "source.png",
            ContentType = "image/png"
        };

        var project = new LiveLayerProject
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OriginalImageId = image.Id,
            Title = "Concurrent token test"
        };

        setup.Users.Add(user);
        setup.Images.Add(image);
        setup.LiveLayerProjects.Add(project);
        await setup.SaveChangesAsync();

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(45));

        var ct = timeout.Token;
        var queue = new ProcessingJobQueue();

        async Task<(bool Success, Exception? Error)> StartRequest()
        {
            try
            {
                await using var db = new ApplicationDbContext(options);

                var service = new ProjectProcessingService(
                    db,
                    new WorkflowStore(),
                    new StartOnlyProcessor(),
                    queue,
                    NullLogger<ProjectProcessingService>.Instance,
                    new TokenService(db));

                await service.StartAsync(
                    user.Id,
                    project.Id,
                    new ProcessProjectRequest(
                        ProcessingOperation.Upscale,
                        TargetLongEdge: 1920),
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

            // 프로젝트 행을 먼저 잠가 두 요청이 경쟁하도록 만든다.
            await using (var gate = new ApplicationDbContext(options))
            {
                await using (var gateTx =
                    await gate.Database.BeginTransactionAsync(ct))
                {
                    var locked = await gate.LiveLayerProjects
                        .FromSqlInterpolated(
                            $"SELECT * FROM live_layer_projects WHERE \"Id\" = {project.Id} FOR UPDATE")
                        .AsNoTracking()
                        .ToListAsync(ct);

                    Assert.Single(locked);

                    first = Task.Run(StartRequest, ct);
                    second = Task.Run(StartRequest, ct);

                    // 두 요청이 잠금 해제를 기다릴 시간을 준다.
                    await Task.Delay(400, ct);

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

            Assert.Equal(1, await verify.ProcessingJobs
                .CountAsync(x => x.ProjectId == project.Id, ct));

            Assert.Equal(1, await verify.TokenTransactions
                .CountAsync(x => x.UserId == user.Id, ct));

            var wallet = await verify.UserTokenWallets
                .AsNoTracking()
                .SingleAsync(x => x.UserId == user.Id, ct);

            Assert.Equal(8, wallet.Balance);
            Assert.Equal(0, wallet.PurchasedBalance);

            var transaction = await verify.TokenTransactions
                .AsNoTracking()
                .SingleAsync(x => x.UserId == user.Id, ct);

            Assert.Equal(2, transaction.Amount);
            Assert.Equal(
                TokenTransactionStatus.Reserved,
                transaction.Status);
        }
        finally
        {
            // 테스트 데이터만 삭제. 운영 사용자에는 영향 없음.
            await using var cleanup = new ApplicationDbContext(options);

            await cleanup.TokenTransactions
                .Where(x => x.UserId == user.Id)
                .ExecuteDeleteAsync();

            await cleanup.ProcessingJobs
                .Where(x => x.ProjectId == project.Id)
                .ExecuteDeleteAsync();

            await cleanup.LiveLayerProjects
                .Where(x => x.Id == project.Id)
                .ExecuteDeleteAsync();

            await cleanup.Images
                .Where(x => x.Id == image.Id)
                .ExecuteDeleteAsync();

            await cleanup.UserTokenWallets
                .Where(x => x.UserId == user.Id)
                .ExecuteDeleteAsync();

            await cleanup.Users
                .Where(x => x.Id == user.Id)
                .ExecuteDeleteAsync();
        }
    }

    private sealed class StartOnlyProcessor : IProjectImageProcessor
    {
        public bool Supports(ProcessingOperation operation) => true;

        public Task<ProjectProcessingResult> ProcessAsync(
            ProjectProcessingRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "이 테스트에서는 GPU 작업을 실행하지 않습니다.");
    }
}
