namespace UpscaleLab.Infrastructure.Processing;

public sealed class SeeThroughOptions
{
    public bool Enabled { get; init; }
    public string Provider { get; init; } = "Local";
    public string RunPodEndpointId { get; init; } = string.Empty;
    public string RunPodApiKey { get; init; } = string.Empty;
    public string RunPodApiBaseUrl { get; init; } = "https://api.runpod.ai/v2";
    public int RunPodTimeoutSeconds { get; init; } = 2400;
    public string RepositoryPath { get; init; } = string.Empty;
    public string PythonExecutable { get; init; } = "python";
    public string AdapterScriptPath { get; init; } = string.Empty;
    public int TimeoutMinutes { get; init; } = 30;
}
