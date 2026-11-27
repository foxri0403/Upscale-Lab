using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Projects;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Wallpapers;

namespace UpscaleLab.Infrastructure.Images;

public sealed class WebpImageService(ApplicationDbContext db, WebpVariantStore variants, IStorageService storage,
    ILogger<WebpImageService> logger) : IWebpImageService
{
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(15);
    private sealed record Source(string Key, string ContentType, string? Role = null);
    private sealed record Selection(IReadOnlyList<Source> Sources, bool Layered = false);

    public async Task<WebpImageView> GetAsync(WebpImageKind kind, Guid id, Guid userId, CancellationToken ct)
    {
        var image = await FindAsync(kind, id, userId, ct) ?? throw new NotFoundException("이미지를 찾을 수 없습니다.");
        var files = new List<(string Url, string? Role)>();
        foreach (var source in image.Sources)
        {
            var key = await variants.EnsureAsync(source.Key, source.ContentType, ct);
            files.Add((storage.CreateDownloadUrl(key, UrlLifetime), source.Role));
        }
        if (!image.Layered) return new(files[0].Url);
        return new(files[0].Url, files.FirstOrDefault(file => file.Role == "Background").Url,
            files.Where(file => file.Role != "Background").Select(file => file.Url).ToArray());
    }

    public async Task<bool> PrepareAsync(WebpImageWork item, CancellationToken ct)
    {
        var image = await FindAsync(item.Kind, item.Id, null, ct);
        if (image is null) return false;
        foreach (var source in image.Sources)
            await variants.EnsureAsync(source.Key, source.ContentType, ct);
        return true;
    }

    public async Task<(int Ready, int Failed)> BackfillAsync(CancellationToken ct)
    {
        var ready = 0;
        var failed = 0;
        var all = new (WebpImageKind Kind, IQueryable<Guid> Ids)[]
        {
            (WebpImageKind.Project, db.LiveLayerProjects.AsNoTracking().Select(x => x.Id)),
            (WebpImageKind.Wallpaper, db.WallpaperAssets.AsNoTracking().Where(x => !x.IsArchived).Select(x => x.Id))
        };
        const int batchSize = 32;
        foreach (var (kind, ids) in all)
        {
            for (var offset = 0; ; offset += batchSize)
            {
                var batch = await ids.OrderBy(id => id).Skip(offset).Take(batchSize).ToListAsync(ct);
                foreach (var id in batch)
                {
                    try { if (await PrepareAsync(new(kind, id), ct)) ready++; }
                    catch (Exception error) when (!ct.IsCancellationRequested)
                    {
                        failed++;
                        logger.LogWarning(error, "Could not prepare a stored WebP for {Kind} {ImageId}.", kind, id);
                    }
                }
                if (batch.Count < batchSize) break;
            }
        }
        return (ready, failed);
    }

    private Task<Selection?> FindAsync(WebpImageKind kind, Guid id, Guid? userId, CancellationToken ct) => kind switch
    {
        WebpImageKind.Project => FindProjectAsync(id, userId, ct),
        WebpImageKind.Wallpaper => FindWallpaperAsync(id, userId, ct),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private async Task<Selection?> FindProjectAsync(Guid id, Guid? userId, CancellationToken ct)
    {
        var project = await db.LiveLayerProjects.AsNoTracking()
            .Where(x => x.Id == id && (!userId.HasValue || x.UserId == userId))
            .Select(x => new
            {
                x.SavedEditJson, x.OriginalImage.OriginalObjectKey, x.OriginalImage.ContentType,
                Result = x.ProcessingJobs.Where(job => job.Status == ProcessingJobStatus.Completed && job.ResultObjectKey != null)
                    .OrderByDescending(job => job.CreatedAt).Select(job => job.ResultObjectKey).FirstOrDefault()
            }).SingleOrDefaultAsync(ct);
        if (project is null || ProjectDraftState.IsDraft(project.SavedEditJson)) return null;
        return project.Result is { } result
            ? new([new(result, "image/png")])
            : new([new(project.OriginalObjectKey, project.ContentType)]);
    }

    private async Task<Selection?> FindWallpaperAsync(Guid id, Guid? userId, CancellationToken ct)
    {
        var asset = await db.WallpaperAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived &&
            (!userId.HasValue || x.UserId == userId), ct);
        if (asset is null) return null;
        using var stored = JsonDocument.Parse(asset.ManifestJson);
        if (WallpaperService.IsDeviceVariant(stored.RootElement)) return null;
        if (asset.SourceProjectId is { } projectId &&
            await db.LiveLayerProjects.AsNoTracking().AnyAsync(x => x.Id == projectId && x.UserId == asset.UserId, ct))
            return null; // My Images represents a wallpaper and its source project with one project card.
        var manifest = WallpaperService.ReadManifest(asset);
        if (manifest.Files.Count == 0) return null;
        return new(manifest.Files.OrderBy(file => file.Order)
            .Select(file => new Source(file.Source, file.ContentType, file.Role)).ToArray(), manifest.Mode == WallpaperMode.Live2D);
    }
}
