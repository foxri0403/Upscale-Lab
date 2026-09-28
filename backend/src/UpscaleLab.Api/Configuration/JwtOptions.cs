namespace UpscaleLab.Api.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "UpscaleLab";
    public string Audience { get; set; } = "UpscaleLabClients";
    public string Secret { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 60;
}
