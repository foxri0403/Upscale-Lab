using UpscaleLab.Application.Images;

namespace UpscaleLab.Application.Projects;

public interface IProjectService
{
    Task<ImageContentResponse> ReadContentAsync(Guid userId, Guid projectId, Guid? jobId, Guid? layerId, CancellationToken ct);
    Task<ImageEditState> SaveEditAsync(Guid userId, Guid projectId,
        SaveImageEditRequest request, CancellationToken cancellationToken);
    Task RenameAsync(Guid userId, Guid projectId, RenameProjectRequest request, CancellationToken cancellationToken);
    Task<ProjectDetailResponse> CreateAsync(Guid userId, CreateProjectCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectSummaryResponse>> GetAllAsync(Guid userId, CancellationToken cancellationToken);
    Task<ProjectDetailResponse> GetAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LayerResponse>> GetLayersAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task<LayerResponse> UpdateLayerAsync(Guid userId, Guid projectId, Guid layerId, UpdateLayerRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<LayerResponse>> ReorderLayersAsync(Guid userId, Guid projectId, ReorderProjectLayersRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task DiscardDraftAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task<ProjectDetailResponse> ResetEffectsAsync(Guid userId, Guid projectId, ResetProjectEffectsRequest request, CancellationToken ct);
}
