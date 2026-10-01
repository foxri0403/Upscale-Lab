using System.Net;
using UpscaleLab.Application.Common;
using UpscaleLab.Infrastructure.Auth;
using UpscaleLab.Infrastructure.Email;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class EmailVerificationInfrastructureTests
{
    [Fact]
    public void CodeProtector_HashesAndVerifiesCodeWithoutStoringPlaintext()
    {
        var protector = new EmailVerificationCodeProtector(new EmailVerificationOptions
        {
            CodeHashKey = "test-only-email-code-key-32-bytes-minimum"
        });
        var code = protector.Generate();
        var hash = protector.Hash("USER@example.com", code);

        Assert.Matches("^[0-9]{6}$", code);
        Assert.NotEqual(code, hash);
        Assert.True(protector.Verify("user@example.com", code, hash));
        Assert.False(protector.Verify("user@example.com", "999999", hash));
    }

    [Fact]
    public async Task ResendEmailSender_SendsExpectedAuthenticatedRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") };
        var sender = new ResendEmailSender(client, new EmailVerificationOptions
        {
            ApiKey = "test-api-key",
            FromAddress = "no-reply@example.com",
            FromName = "Upscale Lab"
        });

        await sender.SendVerificationCodeAsync(
            "user@example.com",
            "tester",
            "123456",
            10,
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://api.resend.com/emails", handler.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("test-api-key", handler.AuthorizationParameter);
        Assert.Contains("no-reply@example.com", handler.Body);
        Assert.Contains("user@example.com", handler.Body);
        Assert.Contains("123456", handler.Body);
    }

    [Fact]
    public async Task ResendEmailSender_WhenProviderRejectsRequest_ReturnsServiceUnavailable()
    {
        var handler = new RecordingHandler(HttpStatusCode.TooManyRequests);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") };
        var sender = new ResendEmailSender(client, new EmailVerificationOptions
        {
            ApiKey = "test-api-key",
            FromAddress = "no-reply@example.com"
        });

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => sender.SendVerificationCodeAsync(
            "user@example.com",
            "tester",
            "123456",
            10,
            CancellationToken.None));
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{\"id\":\"test-email-id\"}")
            };
        }
    }
}
