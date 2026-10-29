using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace UpscaleLab.Infrastructure.Payments;

public sealed record TossPaymentResult(
    string PaymentKey,
    string OrderId,
    int TotalAmount,
    string Status
);

public sealed class TossPaymentsClient(
    HttpClient http,
    IConfiguration configuration)
{

    public async Task<TossPaymentResult> GetPaymentAsync(
        string paymentKey,
        CancellationToken ct)
    {
        var secretKey = configuration["TossPayments:SecretKey"];

        if (string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException(
                "토스페이먼츠 시크릿 키가 설정되지 않았습니다.");

        if (string.IsNullOrWhiteSpace(paymentKey) ||
            paymentKey.Length > 200)
            throw new ArgumentException("결제 키가 올바르지 않습니다.");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.tosspayments.com/v1/payments/"
                + Uri.EscapeDataString(paymentKey));

        var encodedKey = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(secretKey + ":"));

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", encodedKey);

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"결제 상태 조회 실패: HTTP {(int)response.StatusCode}");

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct);

        var root = document.RootElement;

        return new TossPaymentResult(
            root.GetProperty("paymentKey").GetString() ?? "",
            root.GetProperty("orderId").GetString() ?? "",
            root.GetProperty("totalAmount").GetInt32(),
            root.GetProperty("status").GetString() ?? ""
        );
    }

    public async Task<TossPaymentResult> ConfirmAsync(
        string paymentKey,
        string orderId,
        int amount,
        CancellationToken ct)
    {
        var secretKey = configuration["TossPayments:SecretKey"];

        if (string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException(
                "토스페이먼츠 시크릿 키가 설정되지 않았습니다.");

        if (string.IsNullOrWhiteSpace(paymentKey) ||
            paymentKey.Length > 200 ||
            string.IsNullOrWhiteSpace(orderId) ||
            orderId.Length > 64 ||
            amount <= 0)
            throw new ArgumentException("결제 승인 요청 정보가 올바르지 않습니다.");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.tosspayments.com/v1/payments/confirm");

        var encodedKey = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(secretKey + ":"));

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", encodedKey);

        // 같은 주문은 항상 같은 멱등키를 사용한다.
        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            "upscalelab-confirm-" + orderId);

        request.Content = JsonContent.Create(new
        {
            paymentKey,
            orderId,
            amount
        });

        using var response = await http.SendAsync(request, ct);

        // 결제 승인 실패 시 키나 민감한 응답 본문은 기록하지 않는다.
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"토스페이먼츠 결제 승인 실패: HTTP {(int)response.StatusCode}");

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct);

        var root = document.RootElement;

        var result = new TossPaymentResult(
            root.GetProperty("paymentKey").GetString() ?? "",
            root.GetProperty("orderId").GetString() ?? "",
            root.GetProperty("totalAmount").GetInt32(),
            root.GetProperty("status").GetString() ?? ""
        );

        // 서버에 기록한 주문정보와 실제 승인 결과를 비교
        if (result.PaymentKey != paymentKey ||
            result.OrderId != orderId ||
            result.TotalAmount != amount ||
            result.Status != "DONE")
            throw new InvalidOperationException(
                "토스페이먼츠 승인 결과가 주문 정보와 일치하지 않습니다.");

        return result;
    }
}
