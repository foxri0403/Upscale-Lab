using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Payments;

namespace UpscaleLab.Api.BackgroundServices;

public sealed class SubscriptionPaymentRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SubscriptionPaymentRecoveryWorker> logger)
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
                    "Payments:EnableSubscriptionRecovery"))
                {
                    await RecoverAsync(stoppingToken);
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
                    ex, "구독 결제 자동 복구 작업 실패");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(
                    stoppingToken))
                    break;
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var billing = scope.ServiceProvider
            .GetRequiredService<TossBillingClient>();

        var fulfillment = scope.ServiceProvider
            .GetRequiredService<SubscriptionFulfillmentService>();

        var renewalFulfillment = scope.ServiceProvider
            .GetRequiredService<SubscriptionRenewalFulfillmentService>();

        var cutoff = DateTime.UtcNow.AddMinutes(-1);

        var orders = await db.PaymentOrders
            .AsNoTracking()
            .Where(x =>
                x.Status == PaymentOrderStatus.Confirming &&
                x.SubscriptionId != null &&
                x.BillingPeriodStartUtc != null &&
                x.CreatedAt <= cutoff)
            .OrderBy(x => x.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        foreach (var order in orders)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                // 결제를 다시 청구하지 않고 상태만 조회
                var payment =
                    await billing.GetPaymentByOrderIdAsync(
                        order.OrderId, ct);

                // 조회 결과의 주문번호가 다르면 상태를 변경하지 않는다.
                if (payment.OrderId != order.OrderId)
                {
                    logger.LogWarning(
                        "결제 조회 주문번호 불일치: {OrderId}",
                        order.OrderId);
                    continue;
                }

                // 실제로 승인 실패 또는 만료가 확인된 경우만 종료.
                // DB 조건부 UPDATE로 다른 Worker의 변경을 보호한다.
                if (payment.Status is "ABORTED" or "EXPIRED")
                {
                    var changed = await db.PaymentOrders
                        .Where(x =>
                            x.Id == order.Id &&
                            x.Status == PaymentOrderStatus.Confirming &&
                            x.SubscriptionId == order.SubscriptionId &&
                            x.BillingPeriodStartUtc ==
                                order.BillingPeriodStartUtc)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(
                                    x => x.Status,
                                    PaymentOrderStatus.Failed)
                                .SetProperty(
                                    x => x.UpdatedAt,
                                    DateTime.UtcNow),
                            ct);

                    if (changed == 1)
                        logger.LogWarning(
                            "구독 결제 실패 확정: {OrderId}, {Status}",
                            order.OrderId, payment.Status);

                    continue;
                }

                // 승인 완료 외 상태는 임의로 실패 확정하지 않는다.
                if (payment.Status != "DONE" ||
                    string.IsNullOrWhiteSpace(payment.PaymentKey) ||
                    payment.TotalAmount != order.Amount ||
                    payment.ApprovedAt is null)
                {
                    continue;
                }

                var subscription = await db.UserSubscriptions
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        s => s.Id == order.SubscriptionId!.Value &&
                             s.UserId == order.UserId,
                        ct);

                if (subscription is null)
                    continue;

                bool granted;

                if (subscription.Status == SubscriptionStatus.Pending)
                {
                    granted = await fulfillment
                        .ActivateInitialSubscriptionAsync(
                            order.UserId, payment, ct);
                }
                else if (subscription.Status == SubscriptionStatus.Active)
                {
                    granted = await renewalFulfillment
                        .CompleteRenewalAsync(
                            order.UserId, payment, ct);
                }
                else
                {
                    // 연체·해지 구독은 임의로 갱신하지 않는다.
                    continue;
                }

                if (granted)
                {
                    logger.LogInformation(
                        "구독 결제 복구 완료: {OrderId}",
                        order.OrderId);
                }
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "구독 결제 복구 실패: {OrderId}, {ErrorType}",
                    order.OrderId,
                    ex.GetType().Name);
            }
        }
    }
}
