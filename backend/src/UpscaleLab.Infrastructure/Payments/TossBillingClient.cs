using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace UpscaleLab.Infrastructure.Payments;

public sealed record TossBillingKeyResult(
    string BillingKey,
    string CustomerKey
);

public sealed record TossBillingPaymentResult(
    string PaymentKey,
    string OrderId,
    int TotalAmount,
    string Status,
    DateTimeOffset? ApprovedAt
);

public sealed class TossBillingClient(
    HttpClient http,
    IConfiguration configuration)
{
    private void SetAuthentication(HttpRequestMessage request)
    {
        var key = configuration["TossPayments:SecretKey"];

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "토스페이먼츠 시크릿 키가 설정되지 않았습니다.");

        var encoded = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(key + ":"));

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", encoded);
    }

    public async Task<TossBillingKeyResult> IssueBillingKeyAsync(
        string authKey,
        string customerKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authKey) ||
            authKey.Length > 300 ||
            string.IsNullOrWhiteSpace(customerKey) ||
            customerKey.Length > 50)
            throw new ArgumentException(
                "자동결제 인증 정보가 올바르지 않습니다.");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.tosspayments.com/v1/billing/authorizations/issue");

        SetAuthentication(request);

        request.Content = JsonContent.Create(new
        {
            authKey,
            customerKey
        });

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"빌링키 발급 실패: HTTP {(int)response.StatusCode}");

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct);

        var root = document.RootElement;

        var result = new TossBillingKeyResult(
            root.GetProperty("billingKey").GetString() ?? "",
            root.GetProperty("customerKey").GetString() ?? ""
        );

        if (string.IsNullOrWhiteSpace(result.BillingKey) ||
            result.CustomerKey != customerKey)
            throw new InvalidOperationException(
                "빌링키 발급 응답 검증에 실패했습니다.");

        return result;
    }


    public async Task<TossBillingPaymentResult> GetPaymentByOrderIdAsync(
        string orderId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId) ||
            orderId.Length > 64)
            throw new ArgumentException("주문번호가 올바르지 않습니다.");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.tosspayments.com/v1/payments/orders/" +
            Uri.EscapeDataString(orderId));

        SetAuthentication(request);

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"구독 결제 조회 실패: HTTP {(int)response.StatusCode}");

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct);

        var root = document.RootElement;

        var result = new TossBillingPaymentResult(
            root.GetProperty("paymentKey").GetString() ?? "",
            root.GetProperty("orderId").GetString() ?? "",
            root.GetProperty("totalAmount").GetInt32(),
            root.GetProperty("status").GetString() ?? "",
            DateTimeOffset.TryParse(
                root.TryGetProperty("approvedAt", out var approved)
                    ? approved.GetString()
                    : null,
                out var approvedAt)
                ? approvedAt
                : null
        );

        if (result.OrderId != orderId)
            throw new InvalidOperationException(
                "구독 결제 조회 결과의 주문번호가 일치하지 않습니다.");

        return result;
    }

    public async Task<TossBillingPaymentResult> ChargeAsync(
        string billingKey,
        string customerKey,
        string orderId,
        string orderName,
        int amount,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(billingKey) ||
            string.IsNullOrWhiteSpace(customerKey) ||
            string.IsNullOrWhiteSpace(orderId) ||
            string.IsNullOrWhiteSpace(orderName) ||
            amount <= 0)
            throw new ArgumentException(
                "정기결제 요청 정보가 올바르지 않습니다.");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.tosspayments.com/v1/billing/" +
            Uri.EscapeDataString(billingKey));

        SetAuthentication(request);

        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key", "UL-billing-" + orderId);

        request.Content = JsonContent.Create(new
        {
            customerKey,
            amount,
            orderId,
            orderName
        });

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"정기결제 승인 실패: HTTP {(int)response.StatusCode}");

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct);

        var root = document.RootElement;

        var result = new TossBillingPaymentResult(
            root.GetProperty("paymentKey").GetString() ?? "",
            root.GetProperty("orderId").GetString() ?? "",
            root.GetProperty("totalAmount").GetInt32(),
            root.GetProperty("status").GetString() ?? "",
            DateTimeOffset.TryParse(
                root.TryGetProperty("approvedAt", out var approved)
                    ? approved.GetString()
                    : null,
                out var approvedAt)
                ? approvedAt
                : null
        );

        if (string.IsNullOrWhiteSpace(result.PaymentKey) ||
            result.OrderId != orderId ||
            result.TotalAmount != amount ||
            result.Status != "DONE")
            throw new InvalidOperationException(
                "정기결제 결과 검증에 실패했습니다.");

        return result;
    }
}
