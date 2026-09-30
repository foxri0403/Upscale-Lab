namespace UpscaleLab.Application.Projects;

public interface IProjectService
{
    Task<ProjectDetailResponse> CreateAsync(Guid userId, CreateProjectCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectSummaryResponse>> GetAllAsync(Guid userId, CancellationToken cancellationToken);
    Task<ProjectDetailResponse> GetAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LayerResponse>> GetLayersAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task<LayerResponse> UpdateLayerAsync(Guid userId, Guid projectId, Guid layerId, UpdateLayerRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
}
