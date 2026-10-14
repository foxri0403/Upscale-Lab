using UpscaleLab.Domain.Enums;
using UpscaleLab.Application.Projects;

namespace UpscaleLab.Application.Processing;

public sealed record LayerProcessingRequest(string InputPath, string OutputDirectory);

public sealed record ProcessedLayer(
    string FilePath,
    LayerType LayerType,
    int LayerOrder,
    double Depth,
    double PositionX,
    double PositionY,
    double Rotation,
    double Scale,
    double MovementX,
    double MovementY);

public interface IImageLayerProcessor
{
    Task<IReadOnlyList<ProcessedLayer>> ProcessAsync(LayerProcessingRequest request, CancellationToken cancellationToken);
}

public interface IProcessingJobQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken);
    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}

public interface IProjectProcessingService
{
    Task<ProcessingStatusResponse> StartAsync(Guid userId, Guid projectId, CancellationToken cancellationToken, ProcessProjectRequest? request = null);
    Task<ProcessingDownloadResponse> GetDownloadAsync(Guid userId, Guid projectId, Guid jobId, CancellationToken cancellationToken);
    Task<ProcessingStatusResponse> GetStatusAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task RecoverPendingAsync(CancellationToken cancellationToken);
    Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken);
}

public sealed record ProcessProjectRequest(
    ProcessingOperation Operation = ProcessingOperation.Layers,
    LayerEngine LayerEngine = LayerEngine.SeeThrough,
    int UpscaleFactor = 2);

public sealed record ProcessingDownloadResponse(string Url, DateTime ExpiresAt, int Width, int Height);

public sealed record ProjectProcessingRequest(
    string InputPath, string OutputDirectory, ProcessingOperation Operation, LayerEngine LayerEngine, int UpscaleFactor);

public sealed record ProjectProcessingResult(
    IReadOnlyList<ProcessedLayer> Layers, string? UpscaledFilePath, int Width, int Height);

public interface IProjectImageProcessor
{
    Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken cancellationToken);
}
