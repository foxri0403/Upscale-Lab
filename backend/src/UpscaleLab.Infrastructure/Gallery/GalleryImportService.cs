using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Gallery;
using UpscaleLab.Application.Projects;
using UpscaleLab.Application.Storage;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Gallery;

public sealed class GalleryImportService(ApplicationDbContext db, IStorageService storage, IProjectService projects)
    : IGalleryImportService
{
    public async Task<ProjectDetailResponse> ImportAsync(Guid userId, Guid postId, CancellationToken cancellationToken)
    {
        var post = await db.GalleryPosts.AsNoTracking().Include(x => x.Image)
            .SingleOrDefaultAsync(x => x.Id == postId && (x.IsPublic || x.UserId == userId), cancellationToken)
            ?? throw new NotFoundException("공유 게시물을 찾을 수 없습니다.");

        // Read the authorized object directly: browser CORS and expiring S3 URLs
        // must not determine whether a public post can be saved to an account.
        await using var content = new MemoryStream();
        await storage.DownloadAsync(post.Image.OriginalObjectKey, content, cancellationToken);
        content.Position = 0;
        return await projects.CreateAsync(userId, new CreateProjectCommand(
            content, post.Image.FileName, post.Image.ContentType, post.Title,
            post.Image.OriginalWidth, post.Image.OriginalHeight, IsDraft: false), cancellationToken);
    }
}
