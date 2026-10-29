using UpscaleLab.Application.Projects;

namespace UpscaleLab.Application.Gallery;

public interface IGalleryImportService
{
    Task<ProjectDetailResponse> ImportAsync(Guid userId, Guid postId, CancellationToken cancellationToken);
}
