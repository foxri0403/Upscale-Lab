using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace UpscaleLab.Infrastructure.Payments;

public sealed record PaymentCard(string? IssuerCode, string? NumberSuffix)
{
    internal static PaymentCard? Read(JsonElement root)
    {
        if (!root.TryGetProperty("card", out var card) || card.ValueKind != JsonValueKind.Object) return null;
        var code = card.TryGetProperty("issuerCode", out var issuer) ? issuer.GetString() : null;
        if (code?.Length != 2 || !code.All(char.IsAsciiLetterOrDigit)) code = null;
        var number = card.TryGetProperty("number", out var masked) ? masked.GetString() : null;
        var suffix = number is { Length: >= 4 } ? number[^4..] : null;
        if (suffix is not null && (!suffix.Any(char.IsAsciiDigit) || !suffix.All(x => char.IsAsciiDigit(x) || x == '*')))
            suffix = null;
        return code is null && suffix is null ? null : new(code, suffix);
    }
}

public sealed record TossCancellation(string Reason, int Amount, DateTimeOffset CanceledAt);
internal sealed class TossCancellationRejectedException : Exception;

public sealed record TossPaymentResult(string PaymentKey, string OrderId, int TotalAmount, string Status,
    PaymentCard? Card = null, IReadOnlyList<TossCancellation>? Cancellations = null);

public sealed class TossPaymentsClient(HttpClient http, IConfiguration configuration)
{
    private const string PaymentsUrl = "https://api.tosspayments.com/v1/payments/";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["TossPayments:SecretKey"]);

    public async Task<TossPaymentResult> GetPaymentAsync(string paymentKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(paymentKey) || paymentKey.Length > 200)
            throw new ArgumentException("결제 키가 올바르지 않습니다.");
        using var request = new HttpRequestMessage(HttpMethod.Get, PaymentsUrl + Uri.EscapeDataString(paymentKey));
        return await SendAsync(request, ct);
    }

    public async Task<TossPaymentResult> ConfirmAsync(string paymentKey, string orderId, int amount, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(paymentKey) || paymentKey.Length > 200 ||
            string.IsNullOrWhiteSpace(orderId) || orderId.Length > 64 || amount <= 0)
            throw new ArgumentException("결제 승인 요청 정보가 올바르지 않습니다.");
        using var request = new HttpRequestMessage(HttpMethod.Post, PaymentsUrl + "confirm");
        request.Headers.Add("Idempotency-Key", "upscalelab-confirm-" + orderId);
        request.Content = JsonContent.Create(new { paymentKey, orderId, amount });
        var result = await SendAsync(request, ct);
        if (result.PaymentKey != paymentKey || result.OrderId != orderId ||
            result.TotalAmount != amount || result.Status != "DONE")
            throw new InvalidOperationException("토스페이먼츠 승인 결과가 주문 정보와 일치하지 않습니다.");
        return result;
    }

    public async Task<TossPaymentResult> CancelAsync(string paymentKey, string orderId, int? partialAmount,
        string reason, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PaymentsUrl + Uri.EscapeDataString(paymentKey) + "/cancel");
        request.Headers.Add("Idempotency-Key", "upscalelab-refund-" + orderId);
        request.Content = partialAmount.HasValue
            ? JsonContent.Create(new { cancelReason = reason, cancelAmount = partialAmount.Value })
            : JsonContent.Create(new { cancelReason = reason });
        return await SendAsync(request, ct, cancellation: true);
    }

    private async Task<TossPaymentResult> SendAsync(HttpRequestMessage request, CancellationToken ct, bool cancellation = false)
    {
        if (!IsConfigured) throw new InvalidOperationException("토스페이먼츠 시크릿 키가 설정되지 않았습니다.");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(configuration["TossPayments:SecretKey"] + ":"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            if (cancellation && (int)response.StatusCode is 400 or 403)
            {
                using var error = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (error.RootElement.TryGetProperty("code", out var code) && code.GetString() is
                    "EXCEED_MAX_REFUND_DUE" or "NOT_ALLOWED_PARTIAL_REFUND" or "NOT_PARTIAL_CANCELABLE_PAYMENT"
                    or "NOT_CANCELABLE_PAYMENT" or "NOT_CANCELABLE_AMOUNT" or "INVALID_VIRTUAL_ACCOUNT_CANCEL_REQUEST_PARAM")
                    throw new TossCancellationRejectedException();
            }
            throw new HttpRequestException($"토스페이먼츠 요청 실패: HTTP {(int)response.StatusCode}");
        }
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = document.RootElement;
        var cancellations = new List<TossCancellation>();
        if (root.TryGetProperty("cancels", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in entries.EnumerateArray())
            {
                if (item.TryGetProperty("cancelStatus", out var status) && status.GetString() != "DONE") continue;
                if (DateTimeOffset.TryParse(item.GetProperty("canceledAt").GetString(), out var at))
                    cancellations.Add(new(item.GetProperty("cancelReason").GetString() ?? "",
                        item.GetProperty("cancelAmount").GetInt32(), at));
            }
        }
        return new(root.GetProperty("paymentKey").GetString() ?? "", root.GetProperty("orderId").GetString() ?? "",
            root.GetProperty("totalAmount").GetInt32(), root.GetProperty("status").GetString() ?? "",
            PaymentCard.Read(root), cancellations);
    }
}
