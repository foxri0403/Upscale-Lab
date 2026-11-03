using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Tokens;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class PostgresTokenIntegrationTests
{
    [Fact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task Reserve_Complete_Refund_UsesRealPostgres()
    {
        var connection = Environment.GetEnvironmentVariable(
            "UPSCALELAB_TOKEN_TEST_CONNECTION");

        Assert.False(
            string.IsNullOrWhiteSpace(connection),
            "테스트 전용 연결 문자열이 필요합니다.");

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connection);

        Assert.Equal(
            "upscalelab_token_test",
            builder.Database);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);

        await db.Database.OpenConnectionAsync();

        Assert.Equal(
            "upscalelab_token_test",
            db.Database.GetDbConnection().Database);

        await using var tx = await db.Database.BeginTransactionAsync();

        var suffix = Guid.NewGuid().ToString("N")[..12];

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"token-test-{suffix}@example.invalid",
            Username = $"token-test-{suffix}",
            PasswordHash = "integration-test-only"
        };

        var image = new UpscaleLab.Domain.Entities.Image
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OriginalUrl = "https://example.invalid/test.png",
            OriginalObjectKey = $"tests/{suffix}/original.png",
            OriginalWidth = 1280,
            OriginalHeight = 720,
            FileName = "test.png",
            ContentType = "image/png"
        };

        var project = new LiveLayerProject
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OriginalImageId = image.Id,
            Title = "PostgreSQL token integration test"
        };

        var fhdJob = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Operation = ProcessingOperation.Upscale,
            TargetLongEdge = 1920
        };

        db.Users.Add(user);
        db.Images.Add(image);
        db.LiveLayerProjects.Add(project);
        db.ProcessingJobs.Add(fhdJob);

        await db.SaveChangesAsync();

        var service = new TokenService(db);
        var ct = CancellationToken.None;

        async Task<int> Balance() =>
            await db.UserTokenWallets
                .AsNoTracking()
                .Where(x => x.UserId == user.Id)
                .Select(x => x.Balance)
                .SingleAsync();

        async Task<string> Status(Guid jobId) =>
            (await db.TokenTransactions
                .AsNoTracking()
                .Where(x => x.ProcessingJobId == jobId)
                .SingleAsync()).Status.ToString();

        // 1. FHD 2토큰 차감
        await service.ReserveCostAsync(user.Id, fhdJob.Id, 2, ct);
        Assert.Equal(8, await Balance());
        Assert.Equal("Reserved", await Status(fhdJob.Id));

        // 2. 성공 시 거래 확정
        await service.CompleteAsync(fhdJob.Id, ct);
        Assert.Equal(8, await Balance());
        Assert.Equal("Completed", await Status(fhdJob.Id));

        fhdJob.Status = ProcessingJobStatus.Completed;
        await db.SaveChangesAsync();

        // 3. QHD 4토큰 차감
        var qhdJob = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Operation = ProcessingOperation.Upscale,
            TargetLongEdge = 2560
        };

        db.ProcessingJobs.Add(qhdJob);
        await db.SaveChangesAsync();

        await service.ReserveCostAsync(user.Id, qhdJob.Id, 4, ct);
        Assert.Equal(4, await Balance());
        Assert.Equal("Reserved", await Status(qhdJob.Id));

        // 4. 실패 시 환불
        await service.RefundAsync(qhdJob.Id, ct);
        Assert.Equal(8, await Balance());
        Assert.Equal("Refunded", await Status(qhdJob.Id));

        // 5. 중복 환불 시 추가 지급되지 않음
        await service.RefundAsync(qhdJob.Id, ct);
        Assert.Equal(8, await Balance());

        qhdJob.Status = ProcessingJobStatus.Failed;
        await db.SaveChangesAsync();

        // 6. 토큰 부족 시 차감 거부
        var insufficientJob = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Operation = ProcessingOperation.Upscale,
            TargetLongEdge = 3840
        };

        db.ProcessingJobs.Add(insufficientJob);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReserveCostAsync(
                user.Id, insufficientJob.Id, 99, ct));

        Assert.Equal(8, await Balance());

        Assert.False(
            await db.TokenTransactions
                .AsNoTracking()
                .AnyAsync(x =>
                    x.ProcessingJobId == insufficientJob.Id));

        // 테스트 사용자, 작업, 토큰 거래 모두 롤백
        await tx.RollbackAsync();
    }
}
