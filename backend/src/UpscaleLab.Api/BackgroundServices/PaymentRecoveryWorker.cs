using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Payments;

namespace UpscaleLab.Api.BackgroundServices;

public sealed class PaymentRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PaymentRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        // 서버 시작 직후 및 이후 5분 간격으로 확인
        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(5));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex, "결제 자동 복구 작업 실패");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var toss = scope.ServiceProvider
            .GetRequiredService<TossPaymentsClient>();
        if (!toss.IsConfigured) return;

        var billing = scope.ServiceProvider.GetRequiredService<AccountBillingService>();
        var refundCutoff = DateTime.UtcNow.AddMinutes(-2);
        var refunds = await db.PaymentOrders.AsNoTracking()
            .Where(x => x.Status == PaymentOrderStatus.Refunding && x.UpdatedAt <= refundCutoff)
            .OrderBy(x => x.UpdatedAt).Take(20).ToListAsync(ct);
        foreach (var refund in refunds)
        {
            try
            {
                if (await billing.CompleteRefundAsync(refund, ct) == PaymentOrderStatus.Refunding)
                {
                    await db.PaymentOrders.Where(x => x.Id == refund.Id && x.Status == PaymentOrderStatus.Refunding)
                        .ExecuteUpdateAsync(x => x.SetProperty(o => o.UpdatedAt, DateTime.UtcNow), ct);
                }
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("환불 결과 복구 실패: {OrderId}, 유형: {ErrorType}", refund.OrderId, error.GetType().Name);
            }
        }

        var fulfillment = scope.ServiceProvider
            .GetRequiredService<PaymentFulfillmentService>();

        var cutoff = DateTime.UtcNow.AddMinutes(-1);
        var oldest = DateTime.UtcNow.AddDays(-7);

        var orders = await db.PaymentOrders
            .AsNoTracking()
            .Where(x =>
                x.Status == PaymentOrderStatus.Confirming &&
                x.SubscriptionId == null &&
                x.PaymentKey != null &&
                x.CreatedAt >= oldest &&
                x.CreatedAt <= cutoff)
            .OrderBy(x => x.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        foreach (var order in orders)
        {
            try
            {
                var payment = await toss.GetPaymentAsync(
                    order.PaymentKey!, ct);

                if (payment.Status != "DONE" ||
                    payment.PaymentKey != order.PaymentKey ||
                    payment.OrderId != order.OrderId ||
                    payment.TotalAmount != order.Amount)
                {
                    continue;
                }

                var granted =
                    await fulfillment.FulfillTokenPurchaseAsync(
                        order.UserId, payment, ct);

                if (granted)
                    logger.LogInformation(
                        "미지급 주문 복구 성공: {OrderId}",
                        order.OrderId);
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 개별 주문 오류가 다른 주문의 복구를 막지 않음
                logger.LogWarning(
                    "미지급 주문 복구 실패: {OrderId}, 유형: {ErrorType}",
                    order.OrderId,
                    ex.GetType().Name);
            }
        }
    }
}
