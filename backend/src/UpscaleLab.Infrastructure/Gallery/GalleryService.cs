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
        CancellationToken cancellationToken)
    {
        var posts = await BaseQuery()
            .AsNoTracking()
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
        var image = await dbContext.Images.SingleOrDefaultAsync(
            x => x.Id == request.ImageId && x.UserId == userId,
            cancellationToken) ?? throw new NotFoundException("게시할 이미지를 찾을 수 없습니다.");

        var post = new GalleryPost
        {
            UserId = userId,
            ImageId = image.Id,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim()
        };

        dbContext.GalleryPosts.Add(post);
        await dbContext.SaveChangesAsync(cancellationToken);

        post = await BaseQuery().SingleAsync(x => x.Id == post.Id, cancellationToken);
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
        if (!await dbContext.GalleryPosts.AnyAsync(x => x.Id == postId, cancellationToken))
        {
            throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");
        }

        var comment = new Comment
        {
            UserId = userId,
            GalleryPostId = postId,
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
        if (!await dbContext.GalleryPosts.AnyAsync(x => x.Id == postId, cancellationToken))
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

    public async Task<DownloadUrlResponse> CreateDownloadUrlAsync(Guid postId, CancellationToken cancellationToken)
    {
        var post = await dbContext.GalleryPosts
            .Include(x => x.Image)
            .SingleOrDefaultAsync(x => x.Id == postId, cancellationToken)
            ?? throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");

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

    private static GalleryPostResponse MapPost(GalleryPost post, Guid? currentUserId) => new(
        post.Id,
        post.ImageId,
        post.UserId,
        post.User.Username,
        post.Title,
        post.Description,
        post.Image.OriginalUrl,
        post.Likes.Count,
        post.Comments.Count,
        post.DownloadCount,
        post.CreatedAt,
        currentUserId.HasValue && post.Likes.Any(x => x.UserId == currentUserId.Value));

    private static CommentResponse MapComment(Comment comment) => new(
        comment.Id,
        comment.UserId,
        comment.User.Username,
        comment.Content,
        comment.CreatedAt,
        comment.UpdatedAt);
}
