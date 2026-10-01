namespace UpscaleLab.Infrastructure.Auth;

public sealed class CognitoOptions
{
    public const string SectionName = "Cognito";

    public string Region { get; set; } = "ap-northeast-2";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public int ResendCooldownSeconds { get; set; } = 60;
}
