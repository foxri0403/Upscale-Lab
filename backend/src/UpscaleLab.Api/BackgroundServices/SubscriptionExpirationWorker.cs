using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Tokens;

namespace UpscaleLab.Api.BackgroundServices;

public sealed class SubscriptionExpirationWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SubscriptionExpirationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(5));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (configuration.GetValue<bool>(
                    "Payments:EnableSubscriptionExpiration"))
                {
                    await ProcessAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex, "구독 만료 처리 실패");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;

        var ids = await db.UserSubscriptions
            .AsNoTracking()
            .Where(s =>
                s.Status == SubscriptionStatus.Active &&
                s.CancelAtPeriodEnd &&
                s.CurrentPeriodEndUtc != null &&
                s.CurrentPeriodEndUtc <= now)
            .OrderBy(s => s.CurrentPeriodEndUtc)
            .Select(s => s.Id)
            .Take(20)
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            try
            {
                var strategy = db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx =
                        await db.Database.BeginTransactionAsync(ct);

                    var subscription = await db.UserSubscriptions
                        .FromSqlInterpolated($"""
                            SELECT * FROM user_subscriptions
                            WHERE "Id" = {id}
                            FOR UPDATE
                            """)
                        .SingleOrDefaultAsync(ct);

                    if (subscription is null ||
                        subscription.Status != SubscriptionStatus.Active ||
                        !subscription.CancelAtPeriodEnd ||
                        subscription.CurrentPeriodEndUtc is null ||
                        subscription.CurrentPeriodEndUtc > DateTime.UtcNow)
                    {
                        return;
                    }

                    // 결제 진행 중인 주문이 있으면
                    // 결제 결과 확인 전까지 종료를 보류한다.
                    var hasUnresolvedPayment = await db.PaymentOrders
                        .AnyAsync(o =>
                            o.SubscriptionId == id &&
                            o.Status == PaymentOrderStatus.Confirming,
                            ct);

                    if (hasUnresolvedPayment)
                        return;

                    var period = TokenService.CurrentPeriodUtc();
                    var updatedAt = DateTime.UtcNow;

                    var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE user_token_wallets
                        SET "Plan" = {"Free"},
                            "Balance" = {10},
                            "PeriodStartUtc" = {period},
                            "UpdatedAt" = {updatedAt}
                        WHERE "UserId" = {subscription.UserId}
                        """, ct);

                    if (updated != 1)
                        throw new InvalidOperationException(
                            "구독 종료 시 토큰 지갑을 찾지 못했습니다.");

                    subscription.Status = SubscriptionStatus.Canceled;
                    subscription.NextBillingAtUtc = null;
                    subscription.BillingKeyCiphertext = null;

                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);

                });

                logger.LogInformation(
                    "구독 종료 처리 완료: {SubscriptionId}",
                    id);
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "구독 종료 보류: {SubscriptionId}, {ErrorType}",
                    id, ex.GetType().Name);
            }
        }
    }
}
