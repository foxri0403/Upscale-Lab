namespace UpscaleLab.Infrastructure.Email;

public sealed class EmailVerificationOptions
{
    public const string SectionName = "EmailVerification";

    public string ApiBaseUrl { get; set; } = "https://api.resend.com/";
    public string ApiKey { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Upscale Lab";
    public string CodeHashKey { get; set; } = string.Empty;
    public int CodeExpirationMinutes { get; set; } = 10;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxFailedAttempts { get; set; } = 5;
}
