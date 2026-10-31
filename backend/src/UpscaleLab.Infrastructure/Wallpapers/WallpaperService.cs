using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Storage;
using UpscaleLab.Application.Wallpapers;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Projects;
using UpscaleLab.Infrastructure.Images;

namespace UpscaleLab.Infrastructure.Wallpapers;

public sealed class WallpaperService(ApplicationDbContext db, IStorageService storage, IObjectCopyService copier)
    : IWallpaperService
{
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(15);

    public async Task<WallpaperSummary> SnapshotAsync(Guid userId, Guid assetId, SnapshotWallpaperRequest request, CancellationToken ct)
    {
        if (request.Strength is < 0 or > 100) throw new ValidationException("움직임 강도는 0~100이어야 합니다.");
        var source = await Owned(userId, assetId, ct);
        var manifest = ReadManifest(source);
        if (source.Mode != WallpaperMode.Live2D) return Map(source);
        var presentation = request.Render is null ? null : new LayerPresentation(request.Render, manifest.Width, manifest.Height);
        var files = manifest.Files.Select(file => file.Role == "Background" ? file : file with {
            MovementX = manifest.Width * .035 * request.Strength / 100,
            MovementY = manifest.Height * .035 * request.Strength / 100 }).ToArray();
        var edit = ImageEditState.FromJson(source.SavedEditJson);
        var independentMotion = request.MouseEnabled.HasValue || request.GyroEnabled.HasValue;
        if (presentation is null && manifest.MotionEnabled == request.MotionEnabled && files.SequenceEqual(manifest.Files) &&
            (!request.MouseEnabled.HasValue || request.MouseEnabled == (edit?.MouseEnabled ?? false)) &&
            (!request.GyroEnabled.HasValue || request.GyroEnabled == (edit?.GyroEnabled ?? false))) return Map(source);
        var state = (edit ?? new ImageEditState(ImageEditState.DefaultMotionStrength, "original", "original", 1, true, DateTime.UtcNow, MotionDefaultsVersion: 1))
            with { Strength = request.Strength, LiveEnabled = independentMotion ? edit?.LiveEnabled == true : request.MotionEnabled,
                MouseEnabled = request.MouseEnabled ?? edit?.MouseEnabled ?? false,
                GyroEnabled = request.GyroEnabled ?? edit?.GyroEnabled ?? false, SavedAt = DateTime.UtcNow };
        if (presentation != null) state = state with { Filter = "original", Crop = "original", Scale = 1, Foreground = true, CropRegion = null };
        var asset = new WallpaperAsset { UserId = userId, Title = source.Title, Mode = source.Mode,
            SourceProjectId = source.SourceProjectId, SavedEditJson = state.ToJson() };
        var copied = new List<string>();
        try
        {
            for (var i = 0; i < files.Length; i++)
            {
                var key = $"users/{userId:N}/wallpapers/{asset.Id:N}/{files[i].Order:D3}.png";
                await CopyLayer(files[i].Source, key, presentation, ct); copied.Add(key);
                files[i] = (presentation?.Transform(files[i]) ?? files[i]) with { Source = key };
            }
            var stored = JsonSerializer.SerializeToNode(manifest with { AssetId = asset.Id, Files = files,
                UrlsExpireAt = null, MotionEnabled = request.MotionEnabled,
                Width = presentation?.Width ?? manifest.Width, Height = presentation?.Height ?? manifest.Height,
                MouseEnabled = request.MouseEnabled ?? manifest.MouseEnabled, GyroEnabled = request.GyroEnabled ?? manifest.GyroEnabled })!.AsObject();
            var features = Features(source).Where(feature => independentMotion || feature != "Live 2D").ToList();
            if (!independentMotion && request.MotionEnabled) features.Add("Live 2D");
            stored["_features"] = JsonSerializer.SerializeToNode(features);
            asset.ManifestJson = stored.ToJsonString(); db.WallpaperAssets.Add(asset); await db.SaveChangesAsync(ct);
            return Map(asset);
        }
        catch { foreach (var key in copied) await storage.DeleteAsync(key, CancellationToken.None); throw; }
    }

    public async Task<ImageContentResponse> ReadContentAsync(Guid userId, Guid assetId, int order, CancellationToken ct)
    {
        var asset = await db.WallpaperAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assetId && !x.IsArchived &&
            (x.UserId == userId || x.IsPublic), ct) ?? throw new NotFoundException("배경화면을 찾을 수 없습니다.");
        return await ReadFileAsync(asset, order, ct);
    }

    public async Task<ImageContentResponse> ReadLinkedContentAsync(string token, int order, CancellationToken ct)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) throw new NotFoundException("공유 링크를 찾을 수 없습니다.");
        var asset = await db.WallpaperAssets.AsNoTracking().SingleOrDefaultAsync(x => x.ShareToken == token && !x.IsArchived, ct)
            ?? throw new NotFoundException("공유 링크를 찾을 수 없습니다.");
        return await ReadFileAsync(asset, order, ct);
    }

    private Task<ImageContentResponse> ReadFileAsync(WallpaperAsset asset, int order, CancellationToken ct)
    {
        var file = JsonSerializer.Deserialize<WallpaperManifest>(asset.ManifestJson)!.Files.SingleOrDefault(x => x.Order == order)
            ?? throw new NotFoundException("이미지 파일을 찾을 수 없습니다.");
        return ImageContentReader.ReadAsync(storage, file.Source, file.ContentType, ct);
    }

    public async Task<WallpaperLink> CreateLinkAsync(Guid userId, Guid assetId, CancellationToken ct)
    {
        var asset = await Owned(userId, assetId, ct);
        asset.ShareToken ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await db.SaveChangesAsync(ct);
        return new(asset.ShareToken);
    }

    public async Task<LinkedWallpaper> GetLinkedAsync(string token, CancellationToken ct)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit))
            throw new NotFoundException("공유 링크를 찾을 수 없습니다.");
        var asset = await db.WallpaperAssets.AsNoTracking().SingleOrDefaultAsync(
            x => x.ShareToken == token && !x.IsArchived, ct)
            ?? throw new NotFoundException("공유 링크를 찾을 수 없습니다.");
        var manifest = ReadManifest(asset);
        return new(asset.Title, manifest with {
            Files = manifest.Files.Select(x => x with { Source = storage.CreateDownloadUrl(x.Source, UrlLifetime) }).ToArray(),
            UrlsExpireAt = DateTime.UtcNow.Add(UrlLifetime)
        }, Features(asset));
    }

    public async Task<ImageEditState> SaveEditAsync(Guid userId, Guid assetId, SaveImageEditRequest request, CancellationToken ct)
    {
        var asset = await Owned(userId, assetId, ct);
        if (asset.IsPublic) throw new ConflictException("공유를 끈 뒤 사진 설정을 변경해 주세요.");
        var previous = ImageEditState.FromJson(asset.SavedEditJson);
        var edit = ImageEditState.Create(request, previous);
        if (request.Tags is null) edit = edit with { Tags = previous?.Tags };
        if (request.Title is not null) asset.Title = request.Title.Trim();
        asset.SavedEditJson = edit.ToJson();
        await db.SaveChangesAsync(ct);
        return edit;
    }

    public async Task<WallpaperSummary> ImportAsync(Guid userId, Guid assetId, CancellationToken ct)
    {
        var source = await db.WallpaperAssets.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == assetId && x.IsPublic && !x.IsArchived, ct)
            ?? throw new NotFoundException("공유 배경화면을 찾을 수 없습니다.");
        var manifest = ReadManifest(source);
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
            var latest = project.ProcessingJobs.Where(x => x.Status == ProcessingJobStatus.Completed)
                .OrderByDescending(x => x.CompletedAt).FirstOrDefault();
            if (latest is { ResultWidth: > 0, ResultHeight: > 0 }) { width = latest.ResultWidth; height = latest.ResultHeight; }
            sources.AddRange(project.Layers.OrderBy(x => x.LayerOrder).Select(x => new WallpaperFile(x.ObjectKey,
                "image/png", x.LayerOrder, x.LayerType.ToString(), x.Depth, x.PositionX, x.PositionY,
                x.Rotation, x.Scale, x.MovementX, x.MovementY)));
        }
        var origin = request.OriginProjectId ?? project.Id;
        var metadataProject = origin == project.Id ? project : await db.LiveLayerProjects
            .Include(x => x.Layers).Include(x => x.ProcessingJobs).SingleAsync(x => x.Id == origin && x.UserId == userId, ct);
        var metadata = ImageEditState.FromJson(metadataProject.SavedEditJson);
        var presentation = request.Mode == WallpaperMode.Live2D && request.Render != null ? new LayerPresentation(request.Render, width, height) : null;
        if (Application.Projects.ProjectDraftState.IsDraft(metadataProject.SavedEditJson))
            throw new ConflictException("이미지를 보관함에 저장한 뒤 공유하거나 적용해 주세요.");
        var independentMotion = request.MouseEnabled.HasValue || request.GyroEnabled.HasValue;
        var features = ProjectFeatures.For(metadataProject).Where(feature => independentMotion || request.MotionEnabled || feature != "Live 2D").ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            Project = project.Id, Origin = origin, Title = request.Title.Trim(), request.Mode, request.MotionEnabled, width, height, sources,
            Tags = metadata?.Tags, Features = features, request.MouseEnabled, request.GyroEnabled, request.Render
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
                    return Map(await db.WallpaperAssets.AsNoTracking().SingleAsync(x => x.Id == candidate.Id, ct));
            }
        }
        var asset = new WallpaperAsset { UserId = userId, SourceProjectId = origin,
            Title = request.Title.Trim(), Mode = request.Mode };
        asset.SavedEditJson = new ImageEditState(request.Render?.Strength ?? metadata?.Strength ?? ImageEditState.DefaultMotionStrength, "original", "original", 1, true,
            DateTime.UtcNow, metadata?.Tags, independentMotion ? metadata?.LiveEnabled == true : request.Mode == WallpaperMode.Live2D && request.MotionEnabled,
            request.MouseEnabled ?? metadata?.MouseEnabled ?? false, request.GyroEnabled ?? metadata?.GyroEnabled ?? false, MotionDefaultsVersion: 1).ToJson();
        var copied = new List<string>();
        try
        {
            var files = new List<WallpaperFile>();
            foreach (var source in sources)
            {
                var key = $"users/{userId:N}/wallpapers/{asset.Id:N}/{source.Order:D3}{Path.GetExtension(source.Source)}";
                await CopyLayer(source.Source, key, presentation, ct);
                copied.Add(key);
                files.Add((presentation?.Transform(source) ?? source) with { Source = key });
            }
            var stored = JsonSerializer.SerializeToNode(new WallpaperManifest(1, asset.Id, asset.Mode,
                presentation?.Width ?? width, presentation?.Height ?? height, files, MotionEnabled: request.MotionEnabled,
                MouseEnabled: independentMotion ? request.MouseEnabled ?? metadata?.MouseEnabled ?? false : null,
                GyroEnabled: independentMotion ? request.GyroEnabled ?? metadata?.GyroEnabled ?? false : null))!.AsObject();
            stored["_sourceFingerprint"] = fingerprint;
            stored["_features"] = JsonSerializer.SerializeToNode(features);
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
        var assets = await db.WallpaperAssets.AsNoTracking().Where(x => !x.IsArchived && (shared ? x.IsPublic : x.UserId == userId))
            .OrderByDescending(x => x.CreatedAt).Take(100)
            .ToListAsync(ct);
        var authorIds = assets.Select(x => x.UserId).Distinct().ToArray();
        var authors = await db.Users.AsNoTracking().Where(x => authorIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Username, x.ProfileImageObjectKey }).ToDictionaryAsync(x => x.Id, ct);
        return assets.Select(x => {
            var result = shared ? new WallpaperSummary(x.Id, x.Title, x.Mode, x.IsPublic, x.CreatedAt, Edit: ImageEditState.FromJson(x.SavedEditJson), Features: Features(x)) : Map(x);
            var author = authors.GetValueOrDefault(x.UserId);
            return result with { UserId = x.UserId, Username = author?.Username,
                ProfileImageUrl = author?.ProfileImageObjectKey is { } key ? storage.CreateDownloadUrl(key, UrlLifetime) : null };
        }).ToList();
    }

    public async Task<WallpaperManifest> GetManifestAsync(Guid? userId, Guid assetId, bool agentAccess, CancellationToken ct)
    {
        var asset = await db.WallpaperAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assetId &&
            ((x.UserId == userId && (agentAccess || !x.IsArchived)) || (x.IsPublic && !x.IsArchived)), ct)
            ?? throw new NotFoundException("배경화면을 찾을 수 없습니다.");
        var manifest = ReadManifest(asset);
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
    private async Task CopyLayer(string source, string key, LayerPresentation? presentation, CancellationToken ct)
    {
        if (presentation?.ChangesPixels != true) { await copier.CopyAsync(source, key, ct); return; }
        using var input = new MemoryStream(); await storage.DownloadAsync(source, input, ct);
        using var output = new MemoryStream(presentation.Render(input.ToArray())); await storage.UploadAsync(output, key, "image/png", ct);
    }
    private static WallpaperManifest ReadManifest(WallpaperAsset asset)
    {
        var manifest = JsonSerializer.Deserialize<WallpaperManifest>(asset.ManifestJson)
            ?? throw new InvalidOperationException("저장된 manifest가 올바르지 않습니다.");
        var edit = ImageEditState.FromJson(asset.SavedEditJson);
        return manifest.MouseEnabled is null && manifest.GyroEnabled is null && edit is { } && (edit.MouseEnabled || edit.GyroEnabled)
            ? manifest with { MouseEnabled = edit.MouseEnabled, GyroEnabled = edit.GyroEnabled } : manifest;
    }
    private static WallpaperSummary Map(WallpaperAsset x) =>
        new(x.Id, x.Title, x.Mode, x.IsPublic, x.CreatedAt, x.SourceProjectId, ImageEditState.FromJson(x.SavedEditJson), Features(x));

    private static IReadOnlyList<string> Features(WallpaperAsset asset)
    {
        using var json = JsonDocument.Parse(asset.ManifestJson);
        return json.RootElement.TryGetProperty("_features", out var value)
            ? value.Deserialize<string[]>() ?? []
            : asset.Mode == WallpaperMode.Live2D ? ["배경 분리", "Live 2D"] : [];
    }
}
