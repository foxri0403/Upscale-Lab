using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Storage;
using UpscaleLab.Application.Wallpapers;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Wallpapers;

public sealed class WallpaperService(ApplicationDbContext db, IStorageService storage, IObjectCopyService copier)
    : IWallpaperService
{
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(15);

    public async Task<WallpaperSummary> ImportAsync(Guid userId, Guid assetId, CancellationToken ct)
    {
        var source = await db.WallpaperAssets.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == assetId && x.IsPublic && !x.IsArchived, ct)
            ?? throw new NotFoundException("공유 배경화면을 찾을 수 없습니다.");
        var manifest = JsonSerializer.Deserialize<WallpaperManifest>(source.ManifestJson)!;
        var asset = new WallpaperAsset { UserId = userId, Title = source.Title, Mode = source.Mode };
        var copied = new List<string>();
        try
        {
            var files = new List<WallpaperFile>();
            foreach (var item in manifest.Files)
            {
                var key = $"users/{userId:N}/wallpapers/{asset.Id:N}/{item.Order:D3}{Path.GetExtension(item.Source)}";
                await copier.CopyAsync(item.Source, key, ct);
                copied.Add(key);
                files.Add(item with { Source = key });
            }
            asset.ManifestJson = JsonSerializer.Serialize(manifest with { AssetId = asset.Id, Files = files, UrlsExpireAt = null });
            db.WallpaperAssets.Add(asset);
            await db.SaveChangesAsync(ct);
            return Map(asset);
        }
        catch
        {
            foreach (var key in copied) await storage.DeleteAsync(key, CancellationToken.None);
            throw;
        }
    }

    public async Task<WallpaperSummary> CreateAsync(Guid userId, CreateWallpaperRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Mode) || string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 160)
            throw new ValidationException("배경화면 이름 또는 모드가 올바르지 않습니다.");
        var project = await db.LiveLayerProjects.Include(x => x.OriginalImage).Include(x => x.Layers)
            .Include(x => x.ProcessingJobs)
            .SingleOrDefaultAsync(x => x.Id == request.ProjectId && x.UserId == userId, ct)
            ?? throw new NotFoundException("프로젝트를 찾을 수 없습니다.");
        if (request.OriginProjectId.HasValue && !await db.LiveLayerProjects.AnyAsync(
            x => x.Id == request.OriginProjectId && x.UserId == userId, ct))
            throw new NotFoundException("원본 프로젝트를 찾을 수 없습니다.");
        var width = project.OriginalImage.OriginalWidth;
        var height = project.OriginalImage.OriginalHeight;
        var sources = new List<WallpaperFile>();
        if (request.Mode == WallpaperMode.Static)
        {
            var job = request.ProcessingJobId.HasValue
                ? project.ProcessingJobs.SingleOrDefault(x => x.Id == request.ProcessingJobId &&
                    x.Status == ProcessingJobStatus.Completed && x.ResultObjectKey != null)
                    ?? throw new NotFoundException("완료된 업스케일 결과를 찾을 수 없습니다.")
                : null;
            if (job != null) { width = job.ResultWidth; height = job.ResultHeight; }
            sources.Add(new(job?.ResultObjectKey ?? project.OriginalImage.OriginalObjectKey,
                job != null ? "image/png" : project.OriginalImage.ContentType, 0, "image"));
        }
        else
        {
            if (request.ProcessingJobId.HasValue)
                throw new ValidationException("Live 2D는 현재 레이어를 고정합니다. processingJobId는 정적 결과에만 사용하세요.");
            if (project.Layers.Count < 2 || !project.Layers.Any(x => x.LayerType == LayerType.Background))
                throw new ConflictException("배경과 전경 레이어가 먼저 필요합니다.");
            var latest = project.ProcessingJobs.Where(x => x.Status == ProcessingJobStatus.Completed &&
                x.Operation != ProcessingOperation.Upscale).OrderByDescending(x => x.CompletedAt).FirstOrDefault();
            if (latest is { ResultWidth: > 0, ResultHeight: > 0 }) { width = latest.ResultWidth; height = latest.ResultHeight; }
            sources.AddRange(project.Layers.OrderBy(x => x.LayerOrder).Select(x => new WallpaperFile(x.ObjectKey,
                "image/png", x.LayerOrder, x.LayerType.ToString(), x.Depth, x.PositionX, x.PositionY,
                x.Rotation, x.Scale, x.MovementX, x.MovementY)));
        }
        var origin = request.OriginProjectId ?? project.Id;
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            Project = project.Id, Origin = origin, Title = request.Title.Trim(), request.Mode, width, height, sources
        }))));
        if (request.ReuseExisting)
        {
            var prior = await db.WallpaperAssets.AsNoTracking().Where(x => x.UserId == userId && !x.IsArchived &&
                x.SourceProjectId == origin && x.Mode == request.Mode && x.Title == request.Title.Trim())
                .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.ManifestJson, x.Title, x.Mode, x.IsPublic, x.CreatedAt, x.SourceProjectId }).ToListAsync(ct);
            foreach (var candidate in prior)
            {
                using var content = JsonDocument.Parse(candidate.ManifestJson);
                if (content.RootElement.TryGetProperty("_sourceFingerprint", out var saved) && saved.GetString() == fingerprint)
                    return new(candidate.Id, candidate.Title, candidate.Mode, candidate.IsPublic, candidate.CreatedAt, candidate.SourceProjectId);
            }
        }
        var asset = new WallpaperAsset { UserId = userId, SourceProjectId = origin,
            Title = request.Title.Trim(), Mode = request.Mode };
        var copied = new List<string>();
        try
        {
            var files = new List<WallpaperFile>();
            foreach (var source in sources)
            {
                var key = $"users/{userId:N}/wallpapers/{asset.Id:N}/{source.Order:D3}{Path.GetExtension(source.Source)}";
                await copier.CopyAsync(source.Source, key, ct);
                copied.Add(key);
                files.Add(source with { Source = key });
            }
            var stored = JsonSerializer.SerializeToNode(new WallpaperManifest(1, asset.Id, asset.Mode, width, height, files))!.AsObject();
            stored["_sourceFingerprint"] = fingerprint;
            asset.ManifestJson = stored.ToJsonString();
            db.WallpaperAssets.Add(asset);
            await db.SaveChangesAsync(ct);
            return Map(asset);
        }
        catch
        {
            foreach (var key in copied) await storage.DeleteAsync(key, CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<WallpaperSummary>> ListAsync(Guid? userId, bool shared, CancellationToken ct)
    {
        if (!shared && !userId.HasValue) throw new UnauthorizedException("로그인이 필요합니다.");
        return await db.WallpaperAssets.AsNoTracking().Where(x => !x.IsArchived && (shared ? x.IsPublic : x.UserId == userId))
            .OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new WallpaperSummary(x.Id, x.Title, x.Mode, x.IsPublic, x.CreatedAt,
                shared ? null : x.SourceProjectId)).ToListAsync(ct);
    }

    public async Task<WallpaperManifest> GetManifestAsync(Guid? userId, Guid assetId, bool agentAccess, CancellationToken ct)
    {
        var asset = await db.WallpaperAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assetId &&
            ((x.UserId == userId && (agentAccess || !x.IsArchived)) || (x.IsPublic && !x.IsArchived)), ct)
            ?? throw new NotFoundException("배경화면을 찾을 수 없습니다.");
        var manifest = JsonSerializer.Deserialize<WallpaperManifest>(asset.ManifestJson)
            ?? throw new InvalidOperationException("저장된 manifest가 올바르지 않습니다.");
        return manifest with { Files = manifest.Files.Select(x => x with {
            Source = storage.CreateDownloadUrl(x.Source, UrlLifetime) }).ToList(),
            UrlsExpireAt = DateTime.UtcNow.Add(UrlLifetime) };
    }

    public async Task SetSharingAsync(Guid userId, Guid assetId, bool isPublic, CancellationToken ct)
    {
        var asset = await Owned(userId, assetId, ct);
        asset.IsPublic = isPublic;
        await db.SaveChangesAsync(ct);
    }

    public async Task RenameAsync(Guid userId, Guid assetId, RenameWallpaperRequest request, CancellationToken ct)
    {
        var asset = await Owned(userId, assetId, ct);
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 160)
            throw new ValidationException("이미지 이름은 1~160자로 입력해 주세요.");
        if (asset.IsPublic) throw new ConflictException("공유를 끈 뒤 이미지 이름을 변경해 주세요.");
        asset.Title = request.Title.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveAsync(Guid userId, Guid assetId, CancellationToken ct)
    {
        var asset = await Owned(userId, assetId, ct);
        asset.IsArchived = true; asset.IsPublic = false;
        await db.SaveChangesAsync(ct);
    }

    private async Task<WallpaperAsset> Owned(Guid userId, Guid assetId, CancellationToken ct) =>
        await db.WallpaperAssets.SingleOrDefaultAsync(x => x.Id == assetId && x.UserId == userId && !x.IsArchived, ct)
        ?? throw new NotFoundException("배경화면을 찾을 수 없습니다.");
    private static WallpaperSummary Map(WallpaperAsset x) =>
        new(x.Id, x.Title, x.Mode, x.IsPublic, x.CreatedAt, x.SourceProjectId);
}
