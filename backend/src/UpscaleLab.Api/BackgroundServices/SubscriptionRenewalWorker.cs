using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Payments;
using UpscaleLab.Domain.Tokens;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Payments;

namespace UpscaleLab.Api.BackgroundServices;

public sealed class SubscriptionRenewalWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SubscriptionRenewalWorker> logger)
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
                // 테스트 전에는 자동청구를 절대 실행하지 않는다.
                if (configuration.GetValue<bool>(
                    "Payments:EnableRenewalBilling"))
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
                    ex, "월간 자동청구 작업 실패");
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
        var services = scope.ServiceProvider;

        var db = services.GetRequiredService<ApplicationDbContext>();
        var orders = services.GetRequiredService<
            SubscriptionRenewalOrderService>();
        var claims = services.GetRequiredService<
            SubscriptionRenewalChargeClaimService>();
        var billing = services.GetRequiredService<TossBillingClient>();
        var protector = services.GetRequiredService<BillingKeyProtector>();
        var fulfillment = services.GetRequiredService<
            SubscriptionRenewalFulfillmentService>();

        var now = DateTime.UtcNow;

        var dueIds = await db.UserSubscriptions
            .AsNoTracking()
            .Where(x =>
                x.Status == SubscriptionStatus.Active &&
                !x.CancelAtPeriodEnd &&
                x.BillingKeyCiphertext != null &&
                x.NextBillingAtUtc != null &&
                x.NextBillingAtUtc <= now)
            .OrderBy(x => x.NextBillingAtUtc)
            .Select(x => x.Id)
            .Take(20)
            .ToListAsync(ct);

        foreach (var id in dueIds)
        {
            try
            {
                var order = await orders.GetOrCreateDueOrderAsync(
                    id, ct);

                if (order is null ||
                    order.Status != PaymentOrderStatus.Pending)
                    continue;

                var subscription = await db.UserSubscriptions
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == id, ct);

                var productCode = subscription.Plan switch
                {
                    SubscriptionPlan.Plus => "PLUS_MONTHLY",
                    SubscriptionPlan.Pro => "PRO_MONTHLY",
                    _ => throw new InvalidOperationException(
                        "지원하지 않는 구독 플랜")
                };

                var product = PaymentCatalog.Get(productCode);

                if (order.ProductCode != product.Code ||
                    order.Amount != product.Price)
                    throw new InvalidOperationException(
                        "갱신 상품 또는 금액 불일치");

                var billingKey = protector.Decrypt(
                    subscription.BillingKeyCiphertext!,
                    subscription.CustomerKey);

                // 하나의 실행 주체만 결제를 요청할 수 있다.
                var claimed = await claims.TryClaimAsync(
                    subscription.UserId, order.OrderId, ct);

                if (!claimed)
                    continue;

                // 선점에 성공한 요청만 실제 청구한다.
                // 여기서 문제가 발생하면 Confirming을 유지한다.
                var payment = await billing.ChargeAsync(
                    billingKey,
                    subscription.CustomerKey,
                    order.OrderId,
                    product.Name,
                    order.Amount,
                    ct);

                await fulfillment.CompleteRenewalAsync(
                    subscription.UserId, payment, ct);

                logger.LogInformation(
                    "구독 갱신 정산 완료: {OrderId}",
                    order.OrderId);
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "구독 갱신 처리 보류: {SubscriptionId}, {ErrorType}",
                    id,
                    ex.GetType().Name);
            }
        }
    }
}
