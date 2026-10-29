namespace UpscaleLab.Domain.Entities;

public enum TokenTransactionStatus
{
    Reserved,
    Completed,
    Refunded
}

public sealed class TokenTransaction : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid ProcessingJobId { get; set; }

    public int Amount { get; set; }
    public int MonthlyAmount { get; set; }
    public int PurchasedAmount { get; set; }

    public DateTime PeriodStartUtc { get; set; }

    public TokenTransactionStatus Status { get; set; } =
        TokenTransactionStatus.Reserved;

    public DateTime? CompletedAt { get; set; }

    public User User { get; set; } = null!;

    public ProcessingJob ProcessingJob { get; set; } = null!;
}
