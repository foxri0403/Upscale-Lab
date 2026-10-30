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

    public static int WeeklyAllowance(SubscriptionPlan plan) =>
        MonthlyAllowance(plan);

    // 각 사용자 지급 시작 시각으로부터 7일 단위로 계산한다.
    public static DateTime WeeklyPeriodStart(
        DateTime startUtc, DateTime nowUtc)
    {
        if (startUtc.Kind != DateTimeKind.Utc ||
            nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC 시간이 필요합니다.");

        if (nowUtc < startUtc)
            return startUtc;

        var weeks = (nowUtc - startUtc).Ticks /
                    TimeSpan.FromDays(7).Ticks;

        return startUtc.AddDays(weeks * 7);
    }

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
