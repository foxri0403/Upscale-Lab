using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Images;

public sealed class ImageService(ApplicationDbContext dbContext, IStorageService storageService) : IImageService
{
    private static readonly TimeSpan DownloadLifetime = TimeSpan.FromMinutes(15);

    public async Task<IReadOnlyList<ImageResponse>> GetAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var images = await dbContext.Images
            .AsNoTracking()
            .Include(x => x.OptimizedImages)
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var projects = await dbContext.LiveLayerProjects.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.OriginalImageId, x.SavedEditJson }).ToListAsync(cancellationToken);
        var drafts = projects.Where(x => Application.Projects.ProjectDraftState.IsDraft(x.SavedEditJson))
            .Select(x => x.OriginalImageId).ToHashSet();
        return images.Where(x => !drafts.Contains(x.Id)).Select(Map).ToList();
    }

    public async Task<ImageResponse> GetAsync(Guid userId, Guid imageId, CancellationToken cancellationToken)
    {
        var image = await FindOwnedAsync(userId, imageId, true, cancellationToken);
        return Map(image);
    }

    public async Task<ImageResponse> UploadAsync(
        Guid userId,
        ImageUploadCommand command,
        CancellationToken cancellationToken)
    {
        var normalized = await UploadedImage.ReadAsync(command.Content, command.FileName, command.ContentType, cancellationToken);
        await using var pixels = new MemoryStream(normalized.Bytes, false);
        var safeFileName = SanitizeFileName(normalized.FileName);
        var objectKey = $"users/{userId:N}/originals/{Guid.NewGuid():N}/{safeFileName}";
        var stored = await storageService.UploadAsync(pixels, objectKey, normalized.ContentType, cancellationToken);

        try
        {
            var image = new Image
            {
                UserId = userId,
                OriginalUrl = stored.StorageUrl,
                OriginalObjectKey = stored.ObjectKey,
                OriginalWidth = normalized.Width,
                OriginalHeight = normalized.Height,
                FileName = safeFileName,
                ContentType = normalized.ContentType
            };

            dbContext.Images.Add(image);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Map(image);
        }
        catch
        {
            await storageService.DeleteAsync(stored.ObjectKey, CancellationToken.None);
            throw;
        }
    }

    public async Task<DownloadUrlResponse> CreateDownloadUrlAsync(
        Guid userId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        var image = await FindOwnedAsync(userId, imageId, false, cancellationToken);
        var expiresAt = DateTime.UtcNow.Add(DownloadLifetime);
        return new DownloadUrlResponse(storageService.CreateDownloadUrl(image.OriginalObjectKey, DownloadLifetime), expiresAt);
    }

    public async Task DeleteAsync(Guid userId, Guid imageId, CancellationToken cancellationToken)
    {
        var image = await FindOwnedAsync(userId, imageId, true, cancellationToken);
        var objectKeys = image.OptimizedImages.Select(x => x.ObjectKey)
            .Append(image.OriginalObjectKey).Append(image.OriginalObjectKey + ".gallery-480.jpg").ToArray();

        dbContext.Images.Remove(image);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var objectKey in objectKeys)
        {
            await storageService.DeleteAsync(objectKey, cancellationToken);
        }
    }

    private async Task<Image> FindOwnedAsync(
        Guid userId,
        Guid imageId,
        bool includeOptimized,
        CancellationToken cancellationToken)
    {
        IQueryable<Image> query = dbContext.Images;
        if (includeOptimized)
        {
            query = query.Include(x => x.OptimizedImages);
        }

        return await query.SingleOrDefaultAsync(x => x.Id == imageId && x.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("이미지를 찾을 수 없습니다.");
    }

    private static ImageResponse Map(Image image) => new(
        image.Id,
        image.OriginalUrl,
        image.OriginalWidth,
        image.OriginalHeight,
        image.FileName,
        image.ContentType,
        image.CreatedAt,
        image.UpdatedAt,
        image.OptimizedImages
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new OptimizedImageResponse(
                x.Id,
                x.DeviceId,
                x.Width,
                x.Height,
                x.Orientation,
                x.Url,
                x.CreatedAt))
            .ToList());

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "image" : name;
    }
}
