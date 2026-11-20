using UpscaleLab.Infrastructure.Upscaling;

namespace UpscaleLab.Infrastructure.Processing;

// The local server implements the same queue/S3 protocol as the RunPod adapters.
// Selection is explicit: a failed provider never switches to another provider.
public sealed class LocalGpuOptions
{
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:18188/v2/";
    public string ApiKey { get; set; } = "";
    public bool UpscaleEnabled { get; set; } = true;
    public bool LayersEnabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 1800;

    public static RunPodOptions Select(string? provider, RunPodOptions cloud, LocalGpuOptions local)
    {
        if (string.IsNullOrWhiteSpace(provider) || provider.Equals("RunPod", StringComparison.OrdinalIgnoreCase))
            return cloud;
        if (!provider.Equals("LocalGpu", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Processing:Provider must be RunPod or LocalGpu.");

        if (!Uri.TryCreate(local.ApiBaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("LocalGpu:ApiBaseUrl requires HTTPS or a loopback HTTP SSH tunnel.");
        if (local.TimeoutSeconds is < 60 or > 3600)
            throw new InvalidOperationException("LocalGpu:TimeoutSeconds must be between 60 and 3600.");
        if (local.ApiKey.Length > 0 && (local.ApiKey.Length < 32 || local.ApiKey.Any(c => c < 33 || c > 126)))
            throw new InvalidOperationException("LocalGpu:ApiKey must contain at least 32 non-whitespace ASCII characters.");

        return new RunPodOptions
        {
            ApiBaseUrl = local.ApiBaseUrl.TrimEnd('/') + "/",
            ApiKey = local.ApiKey,
            EndpointId = local.UpscaleEnabled ? "local-upscale" : "",
            LayersEndpointId = local.LayersEnabled ? "local-layers" : "",
            LayersApiKey = local.ApiKey,
            UseS3Transfer = true,
            UseJobDeadlinePolicy = true,
            TimeoutSeconds = local.TimeoutSeconds,
            LayersTimeoutSeconds = local.TimeoutSeconds
        };
    }
}
