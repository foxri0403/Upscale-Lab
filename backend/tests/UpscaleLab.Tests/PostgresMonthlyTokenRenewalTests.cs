using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Domain.Tokens;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Tokens;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class PostgresMonthlyTokenRenewalTests
{
    [Fact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task FreeMonthlyRenewal_PreservesPurchasedTokens()
    {
        var connection = Environment.GetEnvironmentVariable(
            "UPSCALELAB_TOKEN_TEST_CONNECTION");

        Assert.False(string.IsNullOrWhiteSpace(connection));

        var cs = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        Assert.Equal("upscalelab_token_test", cs.Database);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.OpenConnectionAsync();

        Assert.Equal("upscalelab_token_test",
            db.Database.GetDbConnection().Database);

        await using var tx = await db.Database.BeginTransactionAsync();

        var user = new User
        {
            Email = $"monthly-{Guid.NewGuid():N}@example.invalid",
            Username = $"monthly-{Guid.NewGuid():N}",
            PasswordHash = "test-only"
        };

        var image = new UpscaleLab.Domain.Entities.Image
        {
            User = user,
            OriginalUrl = "https://example.invalid/source.png",
            OriginalObjectKey = "tests/monthly/source.png",
            OriginalWidth = 1280,
            OriginalHeight = 720,
            FileName = "source.png",
            ContentType = "image/png"
        };

        var project = new LiveLayerProject
        {
            User = user,
            OriginalImage = image,
            Title = "Monthly token renewal test"
        };

        var first = new ProcessingJob
        {
            Project = project,
            Operation = ProcessingOperation.Upscale,
            Status = ProcessingJobStatus.Completed,
            TargetLongEdge = 1920
        };

        var second = new ProcessingJob
        {
            Project = project,
            Operation = ProcessingOperation.Upscale,
            Status = ProcessingJobStatus.Completed,
            TargetLongEdge = 2560
        };

        db.ProcessingJobs.AddRange(first, second);
        await db.SaveChangesAsync();

        var currentPeriod = TokenService.CurrentPeriodUtc();
        var previousPeriod = currentPeriod.AddMonths(-1);
        var now = DateTime.UtcNow;

        // 이전 달 무료 잔액 3개, 구매 토큰 9개
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO user_token_wallets
                ("Id", "UserId", "Plan", "Balance",
                 "PurchasedBalance", "PeriodStartUtc",
                 "CreatedAt", "UpdatedAt")
            VALUES
                ({Guid.NewGuid()}, {user.Id}, {"Free"}, {3},
                 {9}, {previousPeriod}, {now}, {now})
            """);

        var tokens = new TokenService(db);
        var ct = CancellationToken.None;

        async Task<UserTokenWallet> Wallet() =>
            await db.UserTokenWallets
                .AsNoTracking()
                .SingleAsync(x => x.UserId == user.Id);

        // 새 달 첫 사용 시 10개로 갱신한 후 FHD 2개 차감
        await tokens.ReserveCostAsync(user.Id, first.Id, 2, ct);

        var wallet = await Wallet();
        Assert.Equal(SubscriptionPlan.Free, wallet.Plan);
        Assert.Equal(currentPeriod, wallet.PeriodStartUtc);
        Assert.Equal(8, wallet.Balance);
        Assert.Equal(9, wallet.PurchasedBalance);

        // 같은 달 다음 요청에는 10개를 다시 지급하지 않아야 함
        await tokens.ReserveCostAsync(user.Id, second.Id, 4, ct);

        wallet = await Wallet();
        Assert.Equal(4, wallet.Balance);
        Assert.Equal(9, wallet.PurchasedBalance);

        // QHD 요청 환불 시 4개만 복원
        await tokens.RefundAsync(second.Id, ct);

        wallet = await Wallet();
        Assert.Equal(8, wallet.Balance);
        Assert.Equal(9, wallet.PurchasedBalance);

        // 중복 환불 방지
        await tokens.RefundAsync(second.Id, ct);

        wallet = await Wallet();
        Assert.Equal(8, wallet.Balance);
        Assert.Equal(9, wallet.PurchasedBalance);

        Assert.Equal(2, await db.TokenTransactions
            .CountAsync(x => x.UserId == user.Id));

        // 변경된 테스트 데이터 모두 롤백
        await tx.RollbackAsync();
    }
}
