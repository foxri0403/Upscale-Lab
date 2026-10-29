namespace UpscaleLab.Infrastructure.Inpainting;

public sealed class RunPodInpaintingOptions
{
    public bool Enabled { get; set; } = false;

    public string ApiKey { get; set; } = "";

    public string EndpointId { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 1200;
}
