using UpscaleLab.Application.Images;

namespace UpscaleLab.Application.Gallery;

public interface IGalleryService
{
    Task<IReadOnlyList<GalleryPostResponse>> GetAllAsync(
        Guid? currentUserId,
        string? search,
        string? tag,
        CancellationToken cancellationToken,
        string[]? tags = null);
    Task<GalleryPostDetailResponse> GetAsync(Guid postId, Guid? currentUserId, CancellationToken cancellationToken);
    Task<DownloadUrlResponse> CreatePreviewUrlAsync(Guid postId, Guid? currentUserId, CancellationToken cancellationToken);
    Task<GalleryPostResponse> CreateAsync(Guid userId, CreateGalleryPostRequest request, CancellationToken cancellationToken);
    Task<GalleryPostResponse> UpdateTagAsync(
        Guid userId,
        Guid postId,
        UpdateGalleryPostTagRequest request,
        CancellationToken cancellationToken);
    Task<GalleryPostResponse> UpdateAsync(
        Guid userId,
        Guid postId,
        UpdateGalleryPostRequest request,
        CancellationToken cancellationToken);
    Task DeletePostAsync(Guid userId, Guid postId, CancellationToken cancellationToken);
    Task<CommentResponse> AddCommentAsync(Guid userId, Guid postId, CreateCommentRequest request, CancellationToken cancellationToken);
    Task DeleteCommentAsync(Guid userId, Guid commentId, CancellationToken cancellationToken);
    Task LikeAsync(Guid userId, Guid postId, CancellationToken cancellationToken);
    Task UnlikeAsync(Guid userId, Guid postId, CancellationToken cancellationToken);
    Task<DownloadUrlResponse> CreateDownloadUrlAsync(Guid postId, Guid? currentUserId, CancellationToken cancellationToken,
        bool countDownload = true);
    Task<GalleryDownloadCountResponse> RecordDownloadAsync(Guid postId, Guid? currentUserId, CancellationToken cancellationToken);
}
