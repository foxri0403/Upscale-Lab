using UpscaleLab.Domain.Enums;
using UpscaleLab.Application.Projects;

namespace UpscaleLab.Application.Processing;

public sealed record LayerProcessingRequest(string InputPath, string OutputDirectory, Guid JobId = default,
    Func<int, CancellationToken, Task>? ReportProgress = null, string? SubjectMaskPath = null, bool RefineSelection = false);

public sealed record ProcessProjectRequest(ProcessingOperation Operation = ProcessingOperation.Layers, int UpscaleFactor = 2,
    int? TargetLongEdge = null);
public sealed record ProjectProcessingRequest(string InputPath, string OutputDirectory, ProcessingOperation Operation,
    int UpscaleFactor, Guid JobId = default, int? TargetLongEdge = null,
    Func<int, CancellationToken, Task>? ReportProgress = null, string? SubjectMaskPath = null, bool RefineSelection = false);
public sealed record ProjectProcessingResult(IReadOnlyList<ProcessedLayer> Layers, string? ImagePath, int Width, int Height);

// The cloud integration owner implements this boundary; no provider credentials reach clients.
public interface IProjectImageProcessor
{
    bool Supports(ProcessingOperation operation);
    Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken cancellationToken);
}

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
    bool IsConfigured => true;
    Task<IReadOnlyList<ProcessedLayer>> ProcessAsync(LayerProcessingRequest request, CancellationToken cancellationToken);
}

public interface IProcessingJobQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken);
    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}

public interface IProjectProcessingService
{
    Task<ProcessingStatusResponse> RefineSelectionAsync(Guid userId, Guid projectId, Guid baseLayerId,
        Stream selection, CancellationToken cancellationToken);
    Task<ProcessingStatusResponse> RefineSubjectAsync(Guid userId, Guid projectId, Guid baseLayerId,
        Stream mask, CancellationToken cancellationToken);
    Task<ProcessingStatusResponse> StartAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task<ProcessingStatusResponse> StartAsync(Guid userId, Guid projectId, ProcessProjectRequest request, CancellationToken cancellationToken);
    Task<UpscaleLab.Application.Images.DownloadUrlResponse> GetResultUrlAsync(Guid userId, Guid projectId, Guid jobId, CancellationToken cancellationToken);
    Task<ProcessingStatusResponse> GetStatusAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task RecoverPendingAsync(CancellationToken cancellationToken);
    Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken);
}
