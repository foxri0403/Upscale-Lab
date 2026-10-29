namespace UpscaleLab.Infrastructure.Upscaling;

public sealed class RunPodOptions
{
    public string ApiKey { get; set; } = "";
    public string EndpointId { get; set; } = "";
    public string LayersEndpointId { get; set; } = "";
    public string LayersApiKey { get; set; } = "";
    public int LayersTimeoutSeconds { get; set; } = 900;
    public string ApiBaseUrl { get; set; } = "https://api.runpod.ai/v2/";
    public bool UseS3Transfer { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 600;
}
