namespace UpscaleLab.Domain.Payments;

public sealed record PaymentProduct(
    string Code,
    string Name,
    int Price,
    int Tokens,
    bool IsSubscription
);

public static class PaymentCatalog
{
    private static readonly Dictionary<string, PaymentProduct> Products =
        new(StringComparer.Ordinal)
        {
            ["PLUS_MONTHLY"] = new(
                "PLUS_MONTHLY", "Plus 월 구독",
                4900, 20, true),

            ["PRO_MONTHLY"] = new(
                "PRO_MONTHLY", "Pro 월 구독",
                9900, 40, true),

            ["TOKEN_10"] = new(
                "TOKEN_10", "토큰 10개",
                1900, 10, false),

            ["TOKEN_30"] = new(
                "TOKEN_30", "토큰 30개",
                4900, 30, false),

            ["TOKEN_50"] = new(
                "TOKEN_50", "토큰 50개",
                7900, 50, false)
        };

    public static IReadOnlyCollection<PaymentProduct> All =>
        Products.Values.ToArray();

    public static PaymentProduct Get(string code)
    {
        if (!Products.TryGetValue(code, out var product))
            throw new ArgumentException("지원하지 않는 결제 상품입니다.");

        return product;
    }
}
