using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Common;

namespace UpscaleLab.Infrastructure.Email;

public sealed class ResendEmailSender(
    HttpClient httpClient,
    EmailVerificationOptions options) : IEmailSender
{
    public async Task SendVerificationCodeAsync(
        string recipientEmail,
        string recipientName,
        string code,
        int expiresInMinutes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.FromAddress))
        {
            throw new ConfigurationException(
                "EmailVerification:ApiKey and EmailVerification:FromAddress are required.");
        }

        var safeName = HtmlEncoder.Default.Encode(recipientName);
        var safeCode = HtmlEncoder.Default.Encode(code);
        var from = string.IsNullOrWhiteSpace(options.FromName)
            ? options.FromAddress
            : $"{options.FromName} <{options.FromAddress}>";

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new
            {
                from,
                to = new[] { recipientEmail },
                subject = "[Upscale Lab] 이메일 인증 코드",
                html = $"""
                    <div style="font-family:Arial,sans-serif;line-height:1.6;color:#1f2937">
                      <h2>이메일 인증</h2>
                      <p>{safeName}님, Upscale Lab 회원가입을 완료하려면 아래 인증 코드를 입력해 주세요.</p>
                      <p style="font-size:32px;font-weight:700;letter-spacing:8px">{safeCode}</p>
                      <p>이 코드는 {expiresInMinutes}분 동안 유효합니다.</p>
                      <p>본인이 요청하지 않았다면 이 메일을 무시해 주세요.</p>
                    </div>
                    """,
                text = $"{recipientName}님, Upscale Lab 이메일 인증 코드는 {code}입니다. " +
                    $"이 코드는 {expiresInMinutes}분 동안 유효합니다.",
                tags = new[] { new { name = "category", value = "email_verification" } }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ServiceUnavailableException(
                    "인증 이메일을 전송할 수 없습니다. 잠시 후 다시 시도해 주세요.");
            }
        }
        catch (HttpRequestException)
        {
            throw new ServiceUnavailableException(
                "인증 이메일을 전송할 수 없습니다. 잠시 후 다시 시도해 주세요.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ServiceUnavailableException(
                "인증 이메일을 전송할 수 없습니다. 잠시 후 다시 시도해 주세요.");
        }
    }
}
