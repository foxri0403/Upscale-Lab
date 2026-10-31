namespace UpscaleLab.Domain.Tokens;

public enum SubscriptionPlan
{
    Free,
    Plus,
    Pro
}

public static class TokenPolicy
{
    public static int MonthlyAllowance(SubscriptionPlan plan) =>
        plan switch
        {
            SubscriptionPlan.Free => 10,
            SubscriptionPlan.Plus => 20,
            SubscriptionPlan.Pro => 40,
            _ => throw new ArgumentOutOfRangeException(nameof(plan))
        };

    public static int UpscaleCost(int targetLongEdge) =>
        targetLongEdge switch
        {
            1920 => 1,
            2560 => 2,
            3840 => 4,
            _ => throw new ArgumentOutOfRangeException(
                nameof(targetLongEdge),
                "FHD, QHD, 4K만 지원합니다.")
        };
}
