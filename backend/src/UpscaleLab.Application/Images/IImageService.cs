namespace UpscaleLab.Application.Images;

public interface IImageService
{
    Task<IReadOnlyList<ImageResponse>> GetAllAsync(Guid userId, CancellationToken cancellationToken);
    Task<ImageResponse> GetAsync(Guid userId, Guid imageId, CancellationToken cancellationToken);
    Task<ImageResponse> UploadAsync(Guid userId, ImageUploadCommand command, CancellationToken cancellationToken);
    Task<DownloadUrlResponse> CreateDownloadUrlAsync(Guid userId, Guid imageId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid userId, Guid imageId, CancellationToken cancellationToken);
}
