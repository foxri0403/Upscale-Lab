namespace UpscaleLab.Infrastructure.Processing;

public sealed class SeeThroughOptions
{
    public bool Enabled { get; init; }
    public string Provider { get; init; } = "Local";
    public string RunPodEndpointId { get; init; } = string.Empty;
    public string RunPodApiKey { get; init; } = string.Empty;
    public string RunPodApiBaseUrl { get; init; } = "https://api.runpod.ai/v2";
    public int RunPodTimeoutSeconds { get; init; } = 2400;
    public string LocalGpuApiBaseUrl { get; init; } = "http://127.0.0.1:18188/v2";
    public string LocalGpuApiKey { get; init; } = string.Empty;
    public int LocalGpuTimeoutSeconds { get; init; } = 2400;

    private bool IsLocalGpu => string.Equals(Provider, "LocalGpu", StringComparison.OrdinalIgnoreCase);
    public string ApiBaseUrl => IsLocalGpu ? LocalGpuApiBaseUrl : RunPodApiBaseUrl;
    public string ApiKey => IsLocalGpu ? LocalGpuApiKey : RunPodApiKey;
    public string EndpointId => IsLocalGpu ? "local-character" : RunPodEndpointId;
    public int TimeoutSeconds => IsLocalGpu ? LocalGpuTimeoutSeconds : RunPodTimeoutSeconds;

    public bool IsTransportConfigured => IsLocalGpu
        ? Uri.TryCreate(LocalGpuApiBaseUrl, UriKind.Absolute, out var uri)
          && (uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback))
          && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
          && LocalGpuApiKey.Length >= 32 && LocalGpuApiKey.All(c => c is >= (char)33 and <= (char)126)
          && LocalGpuTimeoutSeconds is >= 60 and <= 3600
        : string.Equals(Provider, "RunPod", StringComparison.OrdinalIgnoreCase)
          && !string.IsNullOrWhiteSpace(RunPodEndpointId) && !string.IsNullOrWhiteSpace(RunPodApiKey);
}
