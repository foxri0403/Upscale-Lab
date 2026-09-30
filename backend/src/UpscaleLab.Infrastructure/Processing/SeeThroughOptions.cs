namespace UpscaleLab.Infrastructure.Processing;

public sealed class SeeThroughOptions
{
    public bool Enabled { get; init; }
    public string RepositoryPath { get; init; } = string.Empty;
    public string PythonExecutable { get; init; } = "python";
    public string AdapterScriptPath { get; init; } = string.Empty;
    public int TimeoutMinutes { get; init; } = 30;
}
