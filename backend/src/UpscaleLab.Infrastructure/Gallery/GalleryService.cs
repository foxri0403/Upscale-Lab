using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Gallery;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Gallery;

public sealed class GalleryService(ApplicationDbContext dbContext, IStorageService storageService) : IGalleryService
{
    private static readonly TimeSpan DownloadLifetime = TimeSpan.FromMinutes(15);

    public async Task<IReadOnlyList<GalleryPostResponse>> GetAllAsync(
        Guid? currentUserId,
        string? search,
        string? tag,
        CancellationToken cancellationToken)
    {
        var query = BaseQuery()
            .AsNoTracking()
            .Where(x => x.IsPublic);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(x => x.Title.ToLower().Contains(normalizedSearch));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var normalizedTag = GalleryTagCatalog.Normalize(tag);
            query = query.Where(x => x.Tag == normalizedTag);
        }

        var posts = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return posts.Select(x => MapPost(x, currentUserId)).ToList();
    }

    public async Task<GalleryPostDetailResponse> GetAsync(
        Guid postId,
        Guid? currentUserId,
        CancellationToken cancellationToken)
    {
        var post = await BaseQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == postId, cancellationToken)
            ?? throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");

        EnsureAccessible(post, currentUserId);

        var comments = post.Comments
            .OrderBy(x => x.CreatedAt)
            .Select(MapComment)
            .ToList();

        return new GalleryPostDetailResponse(MapPost(post, currentUserId), comments);
    }

    public async Task<GalleryPostResponse> CreateAsync(
        Guid userId,
        CreateGalleryPostRequest request,
        CancellationToken cancellationToken)
    {
        LiveLayerProject? project = null;
        if (request.ProjectId.HasValue)
        {
            project = await dbContext.LiveLayerProjects
                .Include(x => x.OriginalImage)
                .SingleOrDefaultAsync(x => x.Id == request.ProjectId && x.UserId == userId, cancellationToken)
                ?? throw new NotFoundException("게시할 프로젝트를 찾을 수 없습니다.");
        }

        var imageId = project?.OriginalImageId ?? request.ImageId
            ?? throw new ValidationException("ImageId 또는 ProjectId가 필요합니다.");
        var image = await dbContext.Images.SingleOrDefaultAsync(
            x => x.Id == imageId && x.UserId == userId,
            cancellationToken) ?? throw new NotFoundException("게시할 이미지를 찾을 수 없습니다.");

        var post = new GalleryPost
        {
            UserId = userId,
            ImageId = image.Id,
            ProjectId = project?.Id,
            Title = request.Title.Trim(),
            Tag = GalleryTagCatalog.Normalize(request.Tag),
            Description = request.Description?.Trim(),
            IsPublic = request.IsPublic
        };

        dbContext.GalleryPosts.Add(post);
        await dbContext.SaveChangesAsync(cancellationToken);

        post = await BaseQuery().SingleAsync(x => x.Id == post.Id, cancellationToken);
        return MapPost(post, userId);
    }

    public async Task<GalleryPostResponse> UpdateTagAsync(
        Guid userId,
        Guid postId,
        UpdateGalleryPostTagRequest request,
        CancellationToken cancellationToken)
    {
        var post = await dbContext.GalleryPosts.SingleOrDefaultAsync(
            x => x.Id == postId,
            cancellationToken) ?? throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");

        if (post.UserId != userId)
        {
            throw new ForbiddenException("본인의 게시글만 수정할 수 있습니다.");
        }

        post.Tag = GalleryTagCatalog.Normalize(request.Tag);
        await dbContext.SaveChangesAsync(cancellationToken);

        post = await BaseQuery().SingleAsync(x => x.Id == postId, cancellationToken);
        return MapPost(post, userId);
    }

    public async Task DeletePostAsync(Guid userId, Guid postId, CancellationToken cancellationToken)
    {
        var post = await dbContext.GalleryPosts.SingleOrDefaultAsync(x => x.Id == postId, cancellationToken)
            ?? throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");

        if (post.UserId != userId)
        {
            throw new ForbiddenException("본인의 게시글만 삭제할 수 있습니다.");
        }

        dbContext.GalleryPosts.Remove(post);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<CommentResponse> AddCommentAsync(
        Guid userId,
        Guid postId,
        CreateCommentRequest request,
        CancellationToken cancellationToken)
    {
        var accessiblePost = await dbContext.GalleryPosts.SingleOrDefaultAsync(
            x => x.Id == postId && (x.IsPublic || x.UserId == userId),
            cancellationToken) ?? throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");

        var comment = new Comment
        {
            UserId = userId,
            GalleryPostId = accessiblePost.Id,
            Content = request.Content.Trim()
        };

        dbContext.Comments.Add(comment);
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Entry(comment).Reference(x => x.User).LoadAsync(cancellationToken);
        return MapComment(comment);
    }

    public async Task DeleteCommentAsync(Guid userId, Guid commentId, CancellationToken cancellationToken)
    {
        var comment = await dbContext.Comments
            .Include(x => x.GalleryPost)
            .SingleOrDefaultAsync(x => x.Id == commentId, cancellationToken)
            ?? throw new NotFoundException("댓글을 찾을 수 없습니다.");

        if (comment.UserId != userId && comment.GalleryPost.UserId != userId)
        {
            throw new ForbiddenException("댓글 작성자 또는 게시글 작성자만 삭제할 수 있습니다.");
        }

        dbContext.Comments.Remove(comment);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task LikeAsync(Guid userId, Guid postId, CancellationToken cancellationToken)
    {
        if (!await dbContext.GalleryPosts.AnyAsync(
                x => x.Id == postId && (x.IsPublic || x.UserId == userId),
                cancellationToken))
        {
            throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");
        }

        if (await dbContext.GalleryLikes.AnyAsync(x => x.GalleryPostId == postId && x.UserId == userId, cancellationToken))
        {
            return;
        }

        dbContext.GalleryLikes.Add(new GalleryLike { GalleryPostId = postId, UserId = userId });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UnlikeAsync(Guid userId, Guid postId, CancellationToken cancellationToken)
    {
        var like = await dbContext.GalleryLikes.SingleOrDefaultAsync(
            x => x.GalleryPostId == postId && x.UserId == userId,
            cancellationToken);

        if (like is null)
        {
            return;
        }

        dbContext.GalleryLikes.Remove(like);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<DownloadUrlResponse> CreateDownloadUrlAsync(
        Guid postId,
        Guid? currentUserId,
        CancellationToken cancellationToken)
    {
        var post = await dbContext.GalleryPosts
            .Include(x => x.Image)
            .SingleOrDefaultAsync(x => x.Id == postId, cancellationToken)
            ?? throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");

        EnsureAccessible(post, currentUserId);

        var expiresAt = DateTime.UtcNow.Add(DownloadLifetime);
        var url = storageService.CreateDownloadUrl(post.Image.OriginalObjectKey, DownloadLifetime);
        post.DownloadCount++;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new DownloadUrlResponse(url, expiresAt);
    }

    private IQueryable<GalleryPost> BaseQuery() => dbContext.GalleryPosts
        .Include(x => x.User)
        .Include(x => x.Image)
        .Include(x => x.Likes)
        .Include(x => x.Comments)
            .ThenInclude(x => x.User);

    private GalleryPostResponse MapPost(GalleryPost post, Guid? currentUserId) => new(
        post.Id,
        post.ImageId,
        post.UserId,
        post.User.Username,
        post.Title,
        post.Description,
        storageService.CreateDownloadUrl(post.Image.OriginalObjectKey, DownloadLifetime),
        post.Likes.Count,
        post.Comments.Count,
        post.DownloadCount,
        post.CreatedAt,
        currentUserId.HasValue && post.Likes.Any(x => x.UserId == currentUserId.Value),
        post.ProjectId,
        post.IsPublic,
        post.Tag);

    private static void EnsureAccessible(GalleryPost post, Guid? currentUserId)
    {
        if (!post.IsPublic && post.UserId != currentUserId)
        {
            throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");
        }
    }

    private static CommentResponse MapComment(Comment comment) => new(
        comment.Id,
        comment.UserId,
        comment.User.Username,
        comment.Content,
        comment.CreatedAt,
        comment.UpdatedAt);
}
