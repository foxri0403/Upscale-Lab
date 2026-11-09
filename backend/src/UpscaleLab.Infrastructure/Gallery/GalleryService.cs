using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        string[]? tags = null,
        ExploreQuery? explore = null)
    {
        var query = dbContext.GalleryPosts
            .AsNoTracking()
            .Where(x => x.IsPublic);

        search = explore?.Search ?? search;
        tags = explore?.Tags ?? tags;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(x => x.Title.ToLower().Contains(normalizedSearch));
        }

        var platforms = GalleryPlatformTags.Normalize(explore?.PlatformTags);
        if (platforms.Length > 0)
        {
            var pc = platforms.Contains("pc");
            var mobile = platforms.Contains("mobile");
            var tablet = platforms.Contains("tablet");
            query = query.Where(x => (pc && x.PlatformTags.Contains("pc")) ||
                (mobile && x.PlatformTags.Contains("mobile")) || (tablet && x.PlatformTags.Contains("tablet")));
        }

        string[]? selected = null;
        if (explore?.Untagged == true)
        {
            tag = GalleryTagCatalog.Unspecified;
            tags = null;
        }
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

        var ordered = explore?.Sort switch
        {
            "likes" => query.OrderByDescending(x => x.Likes.Count),
            "downloads" => query.OrderByDescending(x => x.SaveCount),
            "name" => query.OrderBy(x => x.Title),
            _ => query.OrderByDescending(x => x.CreatedAt)
        };
        var posts = await ordered.ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Take(Math.Clamp(explore?.Limit ?? 100, 1, 100))
            .Select(x => new GalleryPostResponse(
                x.Id, x.ImageId, x.UserId, x.User.Username, x.Title, x.Description,
                x.Image.OriginalObjectKey, x.Likes.Count, x.Comments.Count, x.SaveCount,
                x.CreatedAt, currentUserId.HasValue && x.Likes.Any(l => l.UserId == currentUserId),
                x.ProjectId, x.IsPublic, x.Tag, x.Tags, null, x.User.ProfileImageObjectKey, x.PlatformTags))
            .ToListAsync(cancellationToken);

        return posts.Select(x => x with {
            ImageUrl = storageService.CreateDownloadUrl(x.ImageUrl, DownloadLifetime),
            Tags = GalleryTagCompatibility.Resolve(x.Tag, x.Tags, x.Description),
            ThumbnailUrl = $"/api/gallery/{x.Id}/thumbnail",
            ProfileImageUrl = x.ProfileImageUrl is { } key ? storageService.CreateDownloadUrl(key, DownloadLifetime) : null
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
        var edit = ImageEditState.RequireEditable(project?.SavedEditJson ?? await dbContext.LiveLayerProjects
            .Where(x => x.UserId == userId && x.OriginalImageId == imageId)
            .Select(x => x.SavedEditJson).FirstOrDefaultAsync(cancellationToken));

        var post = new GalleryPost
        {
            UserId = userId,
            ImageId = image.Id,
            ProjectId = project?.Id,
            Title = request.Title.Trim(),
            Tags = GalleryTagCatalog.NormalizeMany(request.Tags, request.Tag),
            PlatformTags = GalleryPlatformTags.Normalize(request.PlatformTags ??
                edit?.PlatformTags),
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
        if (request.PlatformTags is not null)
            post.PlatformTags = GalleryPlatformTags.Normalize(request.PlatformTags);
        post.Tag = post.Tags[0];
        post.Description = GalleryTagCompatibility.WithoutMetadata(post.Description);
        await dbContext.SaveChangesAsync(cancellationToken);

        post = await BaseQuery().SingleAsync(x => x.Id == postId, cancellationToken);
        return MapPost(post, userId);
    }

    public async Task<GalleryPostResponse> UpdateAsync(
        Guid userId, Guid postId, UpdateGalleryPostRequest request, CancellationToken cancellationToken)
    {
        var post = await dbContext.GalleryPosts.Include(x => x.Project)
            .SingleOrDefaultAsync(x => x.Id == postId, cancellationToken)
            ?? throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");
        if (post.UserId != userId)
            throw new ForbiddenException("본인의 게시글만 수정할 수 있습니다.");

        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160)
            throw new ValidationException("이미지 이름은 1~160자로 입력해 주세요.");
        var tags = request.Tags is null
            ? GalleryTagCompatibility.Resolve(post.Tag, post.Tags, post.Description).ToArray()
            : GalleryTagCatalog.NormalizeMany(request.Tags);
        post.Title = title;
        post.Tags = tags;
        if (request.PlatformTags is not null)
            post.PlatformTags = GalleryPlatformTags.Normalize(request.PlatformTags);
        post.Tag = tags[0];
        post.Description = GalleryTagCompatibility.WithoutMetadata(post.Description);
        post.UpdatedAt = DateTime.UtcNow;
        if (post.Project is { } project && project.UserId == userId)
        {
            var edit = ImageEditState.RequireEditable(project.SavedEditJson) ?? ImageEditState.Original();
            project.Title = title;
            project.SavedEditJson = edit.WithTags(post.Tags, request.PlatformTags).ToJson();
            project.UpdatedAt = DateTime.UtcNow;
        }
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

    public async Task<GalleryEngagement> SetLikeAsync(Guid userId, Guid postId, bool liked, CancellationToken cancellationToken)
    {
        var post = dbContext.GalleryPosts.Where(x => x.Id == postId && x.IsPublic && x.UserId != userId);
        if (!await post.AnyAsync(cancellationToken))
            throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");
        var likes = dbContext.GalleryLikes.Where(x => x.GalleryPostId == postId && x.UserId == userId);
        if (liked)
        {
            if (!await likes.AnyAsync(cancellationToken))
            {
                var entry = dbContext.GalleryLikes.Add(new GalleryLike { GalleryPostId = postId, UserId = userId });
                try { await dbContext.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                { entry.State = EntityState.Detached; }
            }
        }
        else if (dbContext.Database.IsRelational()) await likes.ExecuteDeleteAsync(cancellationToken);
        else if (await likes.SingleOrDefaultAsync(cancellationToken) is { } like)
        {
            dbContext.GalleryLikes.Remove(like);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return await post.Select(x => new GalleryEngagement(x.Likes.Count, x.Likes.Any(l => l.UserId == userId)))
            .SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");
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
        CancellationToken cancellationToken,
        bool countDownload = true)
    {
        return await CreatePreviewUrlAsync(postId, currentUserId, cancellationToken);
    }

    public async Task<GalleryDownloadCountResponse> RecordDownloadAsync(
        Guid postId, Guid? currentUserId, CancellationToken cancellationToken)
    {
        // Older clients may still report a local file download. Only library imports count.
        return await dbContext.GalleryPosts.AsNoTracking().Where(x => x.Id == postId &&
            (x.IsPublic || x.UserId == currentUserId))
            .Select(x => new GalleryDownloadCountResponse(x.SaveCount)).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");
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
        post.SaveCount,
        post.CreatedAt,
        currentUserId.HasValue && post.Likes.Any(x => x.UserId == currentUserId.Value),
        post.ProjectId,
        post.IsPublic,
        post.Tag,
        GalleryTagCompatibility.Resolve(post.Tag, post.Tags, post.Description),
        $"/api/gallery/{post.Id}/thumbnail",
        post.User.ProfileImageObjectKey is { } key ? storageService.CreateDownloadUrl(key, DownloadLifetime) : null,
        post.PlatformTags);

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
