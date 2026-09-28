namespace UpscaleLab.Infrastructure.Upscaling;

public sealed class ReplicateOptions
{
    public string ApiBaseUrl { get; set; } = "https://api.replicate.com/v1/";
    public string ApiToken { get; set; } = string.Empty;
    public string Model { get; set; } = "nightmareai/real-esrgan";
}
