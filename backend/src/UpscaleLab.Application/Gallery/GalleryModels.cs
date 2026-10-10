using System.ComponentModel.DataAnnotations;

namespace UpscaleLab.Application.Gallery;

public sealed record CreateGalleryPostRequest(
    Guid? ImageId,
    [param: Required, MaxLength(160)] string Title,
    [param: MaxLength(2000)] string? Description,
    Guid? ProjectId = null,
    bool IsPublic = true,
    [param: MaxLength(50)] string? Tag = null);

public sealed record GalleryPostResponse(
    Guid Id,
    Guid ImageId,
    Guid UserId,
    string Username,
    string Title,
    string? Description,
    string ImageUrl,
    int LikeCount,
    int CommentCount,
    long DownloadCount,
    DateTime CreatedAt,
    bool IsLikedByCurrentUser,
    Guid? ProjectId,
    bool IsPublic,
    string Tag);

public sealed record GalleryPostDetailResponse(
    GalleryPostResponse Post,
    IReadOnlyList<CommentResponse> Comments);

public sealed record CreateCommentRequest([param: Required, MaxLength(1000)] string Content);

public sealed record CommentResponse(
    Guid Id,
    Guid UserId,
    string Username,
    string Content,
    DateTime CreatedAt,
    DateTime UpdatedAt);
