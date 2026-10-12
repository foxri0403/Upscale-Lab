using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
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
        CancellationToken cancellationToken,
        string[]? tags = null)
    {
        var query = dbContext.GalleryPosts
            .AsNoTracking()
            .Where(x => x.IsPublic);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(x => x.Title.ToLower().Contains(normalizedSearch));
        }

        string[]? selected = null;
        if (tags is { Length: > 0 } || !string.IsNullOrWhiteSpace(tag))
        {
            selected = tags is { Length: > 0 }
                ? tags.Select(GalleryTagCatalog.Normalize).Distinct().ToArray()
                : [GalleryTagCatalog.Normalize(tag)];
            var parameter = Expression.Parameter(typeof(GalleryPost), "post");
            Expression matches = Expression.Constant(false);
            foreach (var selectedTag in selected)
            {
                var contains = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(string)],
                    Expression.Property(parameter, nameof(GalleryPost.Tags)), Expression.Constant(selectedTag));
                var legacy = Expression.AndAlso(
                    Expression.Equal(Expression.ArrayLength(Expression.Property(parameter, nameof(GalleryPost.Tags))), Expression.Constant(0)),
                    Expression.Equal(Expression.Property(parameter, nameof(GalleryPost.Tag)), Expression.Constant(selectedTag)));
                // Compatibility descriptions are searchable before the 100-post limit.
                Expression<Func<GalleryPost, bool>> metadata = post => post.Tags.Length <= 1 &&
                    post.Description != null && post.Description.Contains(GalleryTagCompatibility.Marker) &&
                    post.Description.Contains("\"" + selectedTag + "\"");
                var embedded = new ReplaceParameter(metadata.Parameters[0], parameter).Visit(metadata.Body)!;
                matches = Expression.OrElse(matches, Expression.OrElse(Expression.OrElse(contains, legacy), embedded));
            }
            query = query.Where(Expression.Lambda<Func<GalleryPost, bool>>(matches, parameter));
        }

        var posts = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .Select(x => new GalleryPostResponse(
                x.Id, x.ImageId, x.UserId, x.User.Username, x.Title, x.Description,
                x.Image.OriginalObjectKey, x.Likes.Count, x.Comments.Count, x.DownloadCount,
                x.CreatedAt, currentUserId.HasValue && x.Likes.Any(l => l.UserId == currentUserId),
                x.ProjectId, x.IsPublic, x.Tag, x.Tags, null))
            .ToListAsync(cancellationToken);

        return posts.Select(x => x with {
            ImageUrl = storageService.CreateDownloadUrl(x.ImageUrl, DownloadLifetime),
            Tags = GalleryTagCompatibility.Resolve(x.Tag, x.Tags, x.Description),
            ThumbnailUrl = $"/api/gallery/{x.Id}/thumbnail"
        }).Where(x => selected is null || x.Tags.Any(selected.Contains)).ToList();
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
            Tags = GalleryTagCatalog.NormalizeMany(request.Tags, request.Tag),
            Description = request.Description?.Trim(),
            IsPublic = request.IsPublic
        };

        post.Tag = post.Tags[0];
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

        post.Tags = GalleryTagCatalog.NormalizeMany(request.Tags, request.Tag);
        post.Tag = post.Tags[0];
        post.Description = GalleryTagCompatibility.WithoutMetadata(post.Description);
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

    public async Task<DownloadUrlResponse> CreatePreviewUrlAsync(
        Guid postId,
        Guid? currentUserId,
        CancellationToken cancellationToken)
    {
        var post = await dbContext.GalleryPosts
            .AsNoTracking()
            .Include(x => x.Image)
            .SingleOrDefaultAsync(x => x.Id == postId, cancellationToken)
            ?? throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");

        EnsureAccessible(post, currentUserId);
        return new DownloadUrlResponse(
            storageService.CreateDownloadUrl(post.Image.OriginalObjectKey, DownloadLifetime),
            DateTime.UtcNow.Add(DownloadLifetime));
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
        post.Tag,
        GalleryTagCompatibility.Resolve(post.Tag, post.Tags, post.Description),
        $"/api/gallery/{post.Id}/thumbnail");

    private static void EnsureAccessible(GalleryPost post, Guid? currentUserId)
    {
        if (!post.IsPublic && post.UserId != currentUserId)
        {
            throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");
        }
    }

    private sealed class ReplaceParameter(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : node;
    }

    private static CommentResponse MapComment(Comment comment) => new(
        comment.Id,
        comment.UserId,
        comment.User.Username,
        comment.Content,
        comment.CreatedAt,
        comment.UpdatedAt);
}
