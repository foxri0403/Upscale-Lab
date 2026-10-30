using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Payments;
using UpscaleLab.Domain.Tokens;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Payments;

public sealed class SubscriptionRenewalFulfillmentService(
    ApplicationDbContext db)
{
    // 토스페이먼츠에서 직접 검증한 승인 결과만 전달한다.
    public async Task<bool> CompleteRenewalAsync(
        Guid userId,
        TossBillingPaymentResult payment,
        CancellationToken ct)
    {
        if (payment.Status != "DONE" ||
            string.IsNullOrWhiteSpace(payment.PaymentKey) ||
            string.IsNullOrWhiteSpace(payment.OrderId) ||
            payment.ApprovedAt is null)
            throw new InvalidOperationException(
                "유효한 결제 완료 정보가 아닙니다.");

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx =
                await db.Database.BeginTransactionAsync(ct);

            // 주문 잠금으로 중복 지급 방지
            var order = await db.PaymentOrders
                .FromSqlInterpolated($"""
                    SELECT * FROM payment_orders
                    WHERE "OrderId" = {payment.OrderId}
                      AND "UserId" = {userId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(ct);

            if (order is null ||
                order.SubscriptionId is null ||
                order.BillingPeriodStartUtc is null)
                throw new InvalidOperationException(
                    "구독 갱신 주문을 찾을 수 없습니다.");

            if (order.Amount != payment.TotalAmount)
                throw new InvalidOperationException(
                    "결제 금액이 일치하지 않습니다.");

            if (order.PaymentKey is not null &&
                order.PaymentKey != payment.PaymentKey)
                throw new InvalidOperationException(
                    "결제 키가 일치하지 않습니다.");

            if (order.Status == PaymentOrderStatus.Paid)
            {
                await tx.CommitAsync(ct);
                return false;
            }

            if (order.Status is not (
                PaymentOrderStatus.Pending or
                PaymentOrderStatus.Confirming))
                throw new InvalidOperationException(
                    "갱신할 수 없는 결제 주문 상태입니다.");

            var subscription = await db.UserSubscriptions
                .FromSqlInterpolated($"""
                    SELECT * FROM user_subscriptions
                    WHERE "Id" = {order.SubscriptionId.Value}
                      AND "UserId" = {userId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(ct);

            if (subscription is null ||
                subscription.Status != SubscriptionStatus.Active ||
                subscription.CancelAtPeriodEnd ||
                subscription.NextBillingAtUtc is null ||
                subscription.CurrentPeriodEndUtc is null)
                throw new InvalidOperationException(
                    "갱신할 수 없는 구독 상태입니다.");

            var periodStart = order.BillingPeriodStartUtc.Value;

            if (periodStart != subscription.NextBillingAtUtc.Value ||
                periodStart != subscription.CurrentPeriodEndUtc.Value)
                throw new InvalidOperationException(
                    "구독 결제 기간이 일치하지 않습니다.");

            var productCode = subscription.Plan switch
            {
                SubscriptionPlan.Plus => "PLUS_MONTHLY",
                SubscriptionPlan.Pro => "PRO_MONTHLY",
                _ => throw new InvalidOperationException(
                    "지원하지 않는 플랜입니다.")
            };

            var product = PaymentCatalog.Get(productCode);

            if (!product.IsSubscription ||
                product.Code != order.ProductCode ||
                product.Price != order.Amount)
                throw new InvalidOperationException(
                    "구독 상품 정보가 일치하지 않습니다.");

            var now = DateTime.UtcNow;
            var periodEnd = periodStart.AddMonths(1);
            var approvedAt = payment.ApprovedAt.Value.UtcDateTime;

            // 현재 청구 기간에 승인된 결제만 자동 정산한다.
            if (approvedAt < periodStart ||
                approvedAt >= periodEnd ||
                approvedAt > now.AddMinutes(5) ||
                periodEnd <= now)
                throw new InvalidOperationException(
                    "결제 승인 시각 또는 갱신 기간을 확인해야 합니다.");

            var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE user_token_wallets
                SET "Plan" = {subscription.Plan.ToString()},
                    "Balance" = {product.Tokens},
                    "PeriodStartUtc" = {periodStart},
                    "UpdatedAt" = {now}
                WHERE "UserId" = {userId}
                """, ct);

            if (updated != 1)
                throw new InvalidOperationException(
                    "토큰 지갑 갱신에 실패했습니다.");

            subscription.CurrentPeriodStartUtc = periodStart;
            subscription.CurrentPeriodEndUtc = periodEnd;
            subscription.NextBillingAtUtc = periodEnd;

            order.Status = PaymentOrderStatus.Paid;
            order.PaymentKey = payment.PaymentKey;
            order.PaidAt = approvedAt;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return true;        });
    }
}
