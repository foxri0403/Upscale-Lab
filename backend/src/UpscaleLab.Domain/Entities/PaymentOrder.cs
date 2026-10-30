namespace UpscaleLab.Domain.Entities;

public enum PaymentOrderStatus
{
    Pending,
    Confirming,
    Paid,
    Failed,
    Canceled,
    Refunded
}

public sealed class PaymentOrder : BaseEntity
{
    public Guid UserId { get; set; }

    public string OrderId { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public int Amount { get; set; }

    public PaymentOrderStatus Status { get; set; } =
        PaymentOrderStatus.Pending;

    public string? PaymentKey { get; set; }

    public Guid? SubscriptionId { get; set; }

    public DateTime? BillingPeriodStartUtc { get; set; }

    public DateTime? PaidAt { get; set; }

    public User User { get; set; } = null!;
}
