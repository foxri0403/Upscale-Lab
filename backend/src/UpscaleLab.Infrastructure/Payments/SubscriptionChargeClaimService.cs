using Microsoft.EntityFrameworkCore;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Payments;

public sealed class SubscriptionChargeClaimService(
    ApplicationDbContext db)
{
    public async Task<bool> TryClaimInitialChargeAsync(
        Guid userId,
        string orderId,
        CancellationToken ct)
    {
        if (userId == Guid.Empty ||
            string.IsNullOrWhiteSpace(orderId) ||
            orderId.Length > 64)
            throw new ArgumentException("주문 정보가 올바르지 않습니다.");

        // 조건부 UPDATE는 DB에서 원자적으로 실행된다.
        // Pending 주문만 최초 한 번 Confirming으로 변경한다.
        var changed = await db.PaymentOrders
            .Where(x =>
                x.UserId == userId &&
                x.OrderId == orderId &&
                x.SubscriptionId != null &&
                x.BillingPeriodStartUtc != null &&
                x.Status == PaymentOrderStatus.Pending &&
                db.UserSubscriptions.Any(s =>
                    s.Id == x.SubscriptionId &&
                    s.UserId == userId &&
                    s.Status == SubscriptionStatus.Pending &&
                    s.BillingKeyCiphertext != null))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    x => x.Status,
                    PaymentOrderStatus.Confirming)
                .SetProperty(
                    x => x.UpdatedAt,
                    DateTime.UtcNow),
                ct);

        return changed == 1;
    }
}
