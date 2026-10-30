using UpscaleLab.Domain.Tokens;

namespace UpscaleLab.Domain.Entities;

public enum SubscriptionStatus
{
    Pending,
    Active,
    PastDue,
    Canceled
}

public sealed class UserSubscription : BaseEntity
{
    public Guid UserId { get; set; }

    public SubscriptionPlan Plan { get; set; } =
        SubscriptionPlan.Free;

    public SubscriptionStatus Status { get; set; } =
        SubscriptionStatus.Pending;

    public string CustomerKey { get; set; } =
        string.Empty;

    // 빌링키는 평문이 아닌 암호화된 값으로 저장한다.
    public string? BillingKeyCiphertext { get; set; }

    public DateTime? CurrentPeriodStartUtc { get; set; }

    public DateTime? CurrentPeriodEndUtc { get; set; }

    public DateTime? NextBillingAtUtc { get; set; }

    public bool CancelAtPeriodEnd { get; set; }

    public User User { get; set; } = null!;
}
