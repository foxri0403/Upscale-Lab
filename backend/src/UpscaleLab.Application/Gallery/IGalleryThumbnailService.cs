namespace UpscaleLab.Application.Gallery;

public interface IGalleryThumbnailService
{
    Task<byte[]?> GetAsync(Guid postId, Guid? userId, CancellationToken cancellationToken);
}
