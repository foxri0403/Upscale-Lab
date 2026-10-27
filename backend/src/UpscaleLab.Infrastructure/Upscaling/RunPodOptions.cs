namespace UpscaleLab.Infrastructure.Upscaling;

public sealed class RunPodOptions
{
    public string ApiKey { get; set; } = "";
    public string EndpointId { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "https://api.runpod.ai/v2/";
    public int TimeoutSeconds { get; set; } = 600;
}