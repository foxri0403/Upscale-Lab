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

    // 업스케일링 최초 실행
    public static int UpscaleCost(int targetLongEdge) =>
        targetLongEdge switch
        {
            1920 => 2,
            2560 => 4,
            3840 => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(targetLongEdge))
        };

    // 업스케일링 재수정
    public static int? UpscaleRevisionCost(int targetLongEdge) =>
        targetLongEdge switch
        {
            1920 => 1,
            2560 => 2,
            3840 => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(targetLongEdge))
        };

    // 배경 분리
    public static int LayerCost(int previousCompletedJobs) =>
        previousCompletedJobs == 0 ? 3 : 1;

    // Live2D 레이어 분리
    public static int CharacterLayerCost(int previousCompletedJobs) =>
        previousCompletedJobs == 0 ? 7 : 3;
}
