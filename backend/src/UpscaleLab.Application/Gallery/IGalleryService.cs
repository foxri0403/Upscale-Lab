using UpscaleLab.Application.Images;

namespace UpscaleLab.Application.Gallery;

public interface IGalleryService
{
    Task<IReadOnlyList<GalleryPostResponse>> GetAllAsync(
        Guid? currentUserId,
        string? search,
        string? tag,
        CancellationToken cancellationToken);
    Task<GalleryPostDetailResponse> GetAsync(Guid postId, Guid? currentUserId, CancellationToken cancellationToken);
    Task<GalleryPostResponse> CreateAsync(Guid userId, CreateGalleryPostRequest request, CancellationToken cancellationToken);
    Task DeletePostAsync(Guid userId, Guid postId, CancellationToken cancellationToken);
    Task<CommentResponse> AddCommentAsync(Guid userId, Guid postId, CreateCommentRequest request, CancellationToken cancellationToken);
    Task DeleteCommentAsync(Guid userId, Guid commentId, CancellationToken cancellationToken);
    Task LikeAsync(Guid userId, Guid postId, CancellationToken cancellationToken);
    Task UnlikeAsync(Guid userId, Guid postId, CancellationToken cancellationToken);
    Task<DownloadUrlResponse> CreateDownloadUrlAsync(Guid postId, Guid? currentUserId, CancellationToken cancellationToken);
}
