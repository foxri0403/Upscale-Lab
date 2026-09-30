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
    Task<ProcessingStatusResponse> StartAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task<ProcessingStatusResponse> GetStatusAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task RecoverPendingAsync(CancellationToken cancellationToken);
    Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken);
}
