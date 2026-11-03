using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Payments;
using UpscaleLab.Domain.Tokens;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Payments;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class PostgresSubscriptionMonthlyTests
{
    [Theory]
    [InlineData(SubscriptionPlan.Plus, false, 20)]
    [InlineData(SubscriptionPlan.Pro, false, 40)]
    [InlineData(SubscriptionPlan.Plus, true, 20)]
    [InlineData(SubscriptionPlan.Pro, true, 40)]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task SubscriptionPayment_ReissuesMonthlyTokensOnce(
        SubscriptionPlan plan,
        bool renewal,
        int allowance)
    {
        var connection = Environment.GetEnvironmentVariable(
            "UPSCALELAB_TOKEN_TEST_CONNECTION");

        Assert.False(string.IsNullOrWhiteSpace(connection));

        var cs = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        Assert.Equal("upscalelab_token_test", cs.Database);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection)
            .Options;

        var ct = CancellationToken.None;
        var now = DateTime.UtcNow;
        var code = plan == SubscriptionPlan.Plus
            ? "PLUS_MONTHLY"
            : "PRO_MONTHLY";

        var product = PaymentCatalog.Get(code);
        Assert.Equal(allowance, product.Tokens);

        var userId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var orderId = "UL-TEST-" + Guid.NewGuid().ToString("N");
        var paymentKey = "test-payment-" + Guid.NewGuid().ToString("N");

        // 정기 갱신 시나리오는 이미 활성화된 구독을 구성한다.
        // 결제 예정 시각은 2분 전으로 설정한다.
        var dueStart = now.AddMinutes(-2);
        var approvedAt = new DateTimeOffset(now.AddMinutes(-1));

        var initialStart = approvedAt.UtcDateTime;

        try
        {
            await using (var setup = new ApplicationDbContext(options))
            {
                Assert.Equal(
                    "upscalelab_token_test",
                    setup.Database.GetDbConnection().Database);

                var user = new User
                {
                    Id = userId,
                    Email = $"subscription-{userId:N}@example.invalid",
                    Username = $"sub-{userId:N}",
                    PasswordHash = "test-only"
                };

                var subscription = new UserSubscription
                {
                    Id = subscriptionId,
                    UserId = userId,
                    Plan = plan,
                    Status = renewal
                        ? SubscriptionStatus.Active
                        : SubscriptionStatus.Pending,
                    CustomerKey = "customer-" + userId.ToString("N"),
                    BillingKeyCiphertext = "encrypted-test-only",
                    CurrentPeriodStartUtc = renewal
                        ? dueStart.AddMonths(-1)
                        : null,
                    CurrentPeriodEndUtc = renewal
                        ? dueStart
                        : null,
                    NextBillingAtUtc = renewal
                        ? dueStart
                        : null
                };

                var order = new PaymentOrder
                {
                    UserId = userId,
                    SubscriptionId = subscriptionId,
                    OrderId = orderId,
                    ProductCode = code,
                    Amount = product.Price,
                    Status = PaymentOrderStatus.Pending,
                    BillingPeriodStartUtc = renewal
                        ? dueStart
                        : initialStart
                };

                setup.Users.Add(user);
                setup.UserSubscriptions.Add(subscription);
                setup.PaymentOrders.Add(order);

                await setup.SaveChangesAsync();

                // 기존 잔액과 구매 토큰이 있는 사용자 구성.
                await setup.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO user_token_wallets
                        ("Id", "UserId", "Plan", "Balance",
                         "PurchasedBalance", "PeriodStartUtc",
                         "CreatedAt", "UpdatedAt")
                    VALUES
                        ({Guid.NewGuid()}, {userId},
                         {(renewal ? plan.ToString() : "Free")},
                         {5}, {15},
                         {(renewal ? dueStart.AddMonths(-1) : TokenPeriod())},
                         {now}, {now})
                    """);
            }

            var payment = new TossBillingPaymentResult(
                paymentKey,
                orderId,
                product.Price,
                "DONE",
                approvedAt);

            // 실제 서비스 호출. Toss API로는 요청하지 않는다.
            await using (var db = new ApplicationDbContext(options))
            {
                bool applied = renewal
                    ? await new SubscriptionRenewalFulfillmentService(db)
                        .CompleteRenewalAsync(userId, payment, ct)
                    : await new SubscriptionFulfillmentService(db)
                        .ActivateInitialSubscriptionAsync(userId, payment, ct);

                Assert.True(applied);
            }

            await using (var verify = new ApplicationDbContext(options))
            {
                var wallet = await verify.UserTokenWallets
                    .AsNoTracking()
                    .SingleAsync(x => x.UserId == userId);

                Assert.Equal(plan, wallet.Plan);
                Assert.Equal(allowance, wallet.Balance);
                Assert.Equal(15, wallet.PurchasedBalance);

                Assert.InRange(
                    Math.Abs((
                        wallet.PeriodStartUtc -
                        (renewal ? dueStart : initialStart)
                    ).Ticks),
                    0L, 10L);

                var subscription = await verify.UserSubscriptions
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == subscriptionId);

                Assert.Equal(SubscriptionStatus.Active, subscription.Status);
                Assert.NotNull(subscription.CurrentPeriodEndUtc);
                Assert.InRange(
                    Math.Abs((
                        subscription.CurrentPeriodEndUtc.Value -
                        (renewal ? dueStart : initialStart).AddMonths(1)
                    ).Ticks),
                    0L, 10L);

                var order = await verify.PaymentOrders
                    .AsNoTracking()
                    .SingleAsync(x => x.OrderId == orderId);

                Assert.Equal(PaymentOrderStatus.Paid, order.Status);
                Assert.Equal(paymentKey, order.PaymentKey);

                // 이미 지급된 기간의 잔액을 소비한 것으로 설정.
                // 동일 결제를 재처리해도 잔액이 다시 채워지면 안 된다.
                await verify.UserTokenWallets
                    .Where(x => x.UserId == userId)
                    .ExecuteUpdateAsync(x => x
                        .SetProperty(w => w.Balance, 2));
            }

            await using (var retry = new ApplicationDbContext(options))
            {
                bool appliedAgain = renewal
                    ? await new SubscriptionRenewalFulfillmentService(retry)
                        .CompleteRenewalAsync(userId, payment, ct)
                    : await new SubscriptionFulfillmentService(retry)
                        .ActivateInitialSubscriptionAsync(userId, payment, ct);

                Assert.False(appliedAgain);
            }

            await using (var verifyAgain = new ApplicationDbContext(options))
            {
                var wallet = await verifyAgain.UserTokenWallets
                    .AsNoTracking()
                    .SingleAsync(x => x.UserId == userId);

                Assert.Equal(2, wallet.Balance);
                Assert.Equal(15, wallet.PurchasedBalance);
            }
        }
        finally
        {
            // 테스트 전용 DB에서 본 테스트가 만든 데이터만 정리.
            await using var cleanup = new ApplicationDbContext(options);

            await cleanup.PaymentOrders
                .Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();

            await cleanup.UserSubscriptions
                .Where(x => x.Id == subscriptionId)
                .ExecuteDeleteAsync();

            await cleanup.UserTokenWallets
                .Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();

            await cleanup.Users
                .Where(x => x.Id == userId)
                .ExecuteDeleteAsync();
        }
    }

    private static DateTime TokenPeriod()
    {
        var now = DateTime.UtcNow;
        return new DateTime(
            now.Year, now.Month, 1,
            0, 0, 0, DateTimeKind.Utc);
    }
}
