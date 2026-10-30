using UpscaleLab.Domain.Tokens;

namespace UpscaleLab.Domain.Entities;

public sealed class UserTokenWallet : BaseEntity
{
    public Guid UserId { get; set; }

    public SubscriptionPlan Plan { get; set; } =
        SubscriptionPlan.Free;

    public int Balance { get; set; } = 10;
    public int PurchasedBalance { get; set; } = 0;

    public DateTime PeriodStartUtc { get; set; } =
        new DateTime(DateTime.UtcNow.Year,
                     DateTime.UtcNow.Month,
                     1, 0, 0, 0,
                     DateTimeKind.Utc);

    public User User { get; set; } = null!;
}
