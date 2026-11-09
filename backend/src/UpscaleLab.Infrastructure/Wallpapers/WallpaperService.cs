using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Gallery;
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
        request.Rig?.Validate();
        var source = await Owned(userId, assetId, ct);
        var manifest = ReadManifest(source);
        var edit = EffectiveEdit(source);
        if (edit?.IsReadOnly == true)
        {
            if (request.Render?.Frame is not { } frame)
                throw new ConflictException("다른 사용자의 저장 이미지는 기기 배경화면에만 적용할 수 있습니다.");
            request = new SnapshotWallpaperRequest(manifest.MotionEnabled, edit.Strength, manifest.MouseEnabled, manifest.GyroEnabled,
                new(edit.Strength, edit.Filter, edit.Crop, edit.Scale, edit.Foreground, edit.CropRegion, frame), manifest.Rig);
        }
        if (source.Mode != WallpaperMode.Live2D && request.Render is null) return Map(source);
        var presentation = request.Render is null ? null : new LayerPresentation(request.Render, manifest.Width, manifest.Height);
        var files = manifest.Files.Select(file => file.Role == "Background" ? file : file with {
            MovementX = manifest.Width * .035 * request.Strength / 100,
            MovementY = manifest.Height * .035 * request.Strength / 100 }).ToArray();
        var independentMotion = request.MouseEnabled.HasValue || request.GyroEnabled.HasValue;
        if (presentation is null && manifest.MotionEnabled == request.MotionEnabled && files.SequenceEqual(manifest.Files) &&
            (!request.MouseEnabled.HasValue || request.MouseEnabled == (edit?.MouseEnabled ?? false)) &&
            (!request.GyroEnabled.HasValue || request.GyroEnabled == (edit?.GyroEnabled ?? false)) &&
            request.Rig is null) return Map(source);
        var state = (edit ?? new ImageEditState(ImageEditState.DefaultMotionStrength, "original", "original", 1, true, DateTime.UtcNow, MotionDefaultsVersion: 1))
            with { Strength = request.Strength, LiveEnabled = independentMotion ? edit?.LiveEnabled == true : request.MotionEnabled,
                MouseEnabled = request.MouseEnabled ?? edit?.MouseEnabled ?? false,
                GyroEnabled = request.GyroEnabled ?? edit?.GyroEnabled ?? false,
                Rig = request.Rig ?? edit?.Rig ?? manifest.Rig, SavedAt = DateTime.UtcNow };
        if (presentation != null) state = state with { Filter = "original", Crop = "original", Scale = 1,
            Foreground = true, CropRegion = null, Rig = presentation.TransformRig(state.Rig ?? manifest.Rig) };
        var asset = new WallpaperAsset { UserId = userId, Title = source.Title, Mode = source.Mode,
            SourceProjectId = source.SourceProjectId, SavedEditJson = state.ToJson() };
        var copied = new List<string>();
        try
        {
            for (var i = 0; i < files.Length; i++)
            {
                var extension = presentation?.ChangesPixels == true ? ".png" : Path.GetExtension(files[i].Source);
                var key = $"users/{userId:N}/wallpapers/{asset.Id:N}/{files[i].Order:D3}{extension}";
                var baseLayer = files[i].Role is "Background" or "image";
                await CopyLayer(files[i].Source, key, presentation, ct, baseLayer); copied.Add(key);
                files[i] = (presentation?.Transform(files[i]) ?? files[i]) with { Source = key,
                    ContentType = presentation?.ChangesPixels == true ? "image/png" : files[i].ContentType };
            }
            var stored = JsonSerializer.SerializeToNode(manifest with { AssetId = asset.Id, Files = files,
                UrlsExpireAt = null, MotionEnabled = request.MotionEnabled,
                Width = presentation?.Width ?? manifest.Width, Height = presentation?.Height ?? manifest.Height,
                MouseEnabled = request.MouseEnabled ?? manifest.MouseEnabled, GyroEnabled = request.GyroEnabled ?? manifest.GyroEnabled,
                Rig = presentation?.TransformRig(request.Rig ?? manifest.Rig ?? edit?.Rig) ??
                    request.Rig ?? manifest.Rig ?? edit?.Rig })!.AsObject();
            var features = Features(source).Where(feature => independentMotion || feature != "Live 2D").ToList();
            if (!independentMotion && request.MotionEnabled) features.Add("Live 2D");
            stored["_features"] = JsonSerializer.SerializeToNode(features);
            if (request.Render?.Frame is not null) stored["_deviceVariant"] = true;
            else stored["_ownerSnapshot"] = true;
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
        RequireEditable(asset);
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
        var previous = RequireEditable(asset);
        var edit = ImageEditState.Create(request, previous);
        if (request.Tags is null) edit = edit with { Tags = previous?.Tags };
        if (request.Title is not null) asset.Title = request.Title.Trim();
        asset.SavedEditJson = edit.ToJson();
        await db.SaveChangesAsync(ct);
        return edit;
    }

    public async Task<WallpaperSummary> ImportAsync(Guid userId, Guid assetId, CancellationToken ct)
    {
        var shared = db.WallpaperAssets.Where(x => x.Id == assetId && x.IsPublic && !x.IsArchived);
        var source = await shared.SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("공유 배경화면을 찾을 수 없습니다.");
        var manifest = ReadManifest(source);
        var edit = EffectiveEdit(source);
        if (source.UserId != userId) edit = (edit ?? ImageEditState.Original()) with { IsReadOnly = true };
        var asset = new WallpaperAsset { UserId = userId, Title = source.Title, Mode = source.Mode, SavedEditJson = edit?.ToJson() };
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
            var stored = JsonSerializer.SerializeToNode(manifest with { AssetId = asset.Id, Files = files, UrlsExpireAt = null })!.AsObject();
            stored["_features"] = JsonSerializer.SerializeToNode(Features(source));
            if (source.UserId == userId && edit?.IsReadOnly != true) stored["_ownerSnapshot"] = true;
            asset.ManifestJson = stored.ToJsonString();
            var countSave = source.UserId != userId;
            await using var transaction = countSave && db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(ct) : null;
            if (transaction is not null)
            {
                var changed = await shared.ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.SaveCount, x => x.SaveCount + 1)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
                if (changed == 0) throw new NotFoundException("공유 배경화면을 찾을 수 없습니다.");
            }
            else if (countSave) source.SaveCount++;
            db.WallpaperAssets.Add(asset);
            await db.SaveChangesAsync(ct);
            var result = Map(asset);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return result;
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
        var sourceEdit = ImageEditState.FromJson(project.SavedEditJson);
        if (sourceEdit?.IsReadOnly == true)
        {
            if (request.Render?.Frame is not { } frame || request.Mode != WallpaperMode.Static ||
                request.ProcessingJobId.HasValue || (request.OriginProjectId ?? project.Id) != project.Id)
                throw new ConflictException("다른 사용자의 저장 이미지는 기기 배경화면에만 적용할 수 있습니다.");
            request = request with { Title = project.Title, MotionEnabled = false, MouseEnabled = false, GyroEnabled = false,
                Render = new(sourceEdit.Strength, "original", "original", 1, true, Frame: frame), Rig = null };
        }
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
            if (project.Layers.Count < 2)
                throw new ConflictException("분리된 레이어가 2개 이상 필요합니다.");
            var latest = project.ProcessingJobs.Where(x => x.Status == ProcessingJobStatus.Completed &&
                x.ResultWidth > 0 && x.ResultHeight > 0)
                .OrderByDescending(x => x.CompletedAt).FirstOrDefault();
            if (latest is { ResultWidth: > 0, ResultHeight: > 0 }) { width = latest.ResultWidth; height = latest.ResultHeight; }
            sources.AddRange(project.Layers.OrderBy(x => x.LayerOrder).Select(x => new WallpaperFile(x.ObjectKey,
                "image/png", x.LayerOrder, x.LayerType.ToString(), x.Depth, x.PositionX, x.PositionY,
                x.Rotation, x.Scale, x.MovementX, x.MovementY, x.Id.ToString())));
        }
        var origin = request.OriginProjectId ?? project.Id;
        var metadataProject = origin == project.Id ? project : await db.LiveLayerProjects
            .Include(x => x.Layers).Include(x => x.ProcessingJobs).SingleAsync(x => x.Id == origin && x.UserId == userId, ct);
        var metadata = origin == project.Id ? sourceEdit : ImageEditState.RequireEditable(metadataProject.SavedEditJson);
        request.Rig?.Validate();
        var selectedRig = request.Rig ?? metadata?.Rig;
        var presentation = request.Render is not null &&
            (request.Mode == WallpaperMode.Live2D || request.Render.Frame is not null)
            ? new LayerPresentation(request.Render, width, height) : null;
        if (Application.Projects.ProjectDraftState.IsDraft(metadataProject.SavedEditJson))
            throw new ConflictException("이미지를 보관함에 저장한 뒤 공유하거나 적용해 주세요.");
        var independentMotion = request.MouseEnabled.HasValue || request.GyroEnabled.HasValue;
        var character = ProjectFeatures.HasCharacterLayers(metadataProject);
        var features = ProjectFeatures.For(metadataProject).Where(feature => character ||
            independentMotion || request.MotionEnabled || feature != "Live 2D").ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            Project = project.Id, Origin = origin, Title = request.Title.Trim(), request.Mode, request.MotionEnabled, width, height, sources,
            Tags = metadata?.Tags, PlatformTags = metadata?.PlatformTags, Features = features, request.MouseEnabled, request.GyroEnabled, request.Render, Rig = selectedRig
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
            request.MouseEnabled ?? metadata?.MouseEnabled ?? false, request.GyroEnabled ?? metadata?.GyroEnabled ?? false,
            MotionDefaultsVersion: 1, Rig: presentation?.TransformRig(selectedRig) ?? selectedRig,
            PlatformTags: metadata?.PlatformTags, IsReadOnly: metadata?.IsReadOnly == true).ToJson();
        var copied = new List<string>();
        try
        {
            var files = new List<WallpaperFile>();
            foreach (var source in sources)
            {
                var extension = presentation?.ChangesPixels == true ? ".png" : Path.GetExtension(source.Source);
                var key = $"users/{userId:N}/wallpapers/{asset.Id:N}/{source.Order:D3}{extension}";
                var baseLayer = source.Role is "Background" or "image";
                await CopyLayer(source.Source, key, presentation, ct, baseLayer);
                copied.Add(key);
                files.Add((presentation?.Transform(source) ?? source) with { Source = key,
                    ContentType = presentation?.ChangesPixels == true ? "image/png" : source.ContentType });
            }
            var stored = JsonSerializer.SerializeToNode(new WallpaperManifest(1, asset.Id, asset.Mode,
                presentation?.Width ?? width, presentation?.Height ?? height, files, MotionEnabled: request.MotionEnabled,
                MouseEnabled: independentMotion ? request.MouseEnabled ?? metadata?.MouseEnabled ?? false : null,
                GyroEnabled: independentMotion ? request.GyroEnabled ?? metadata?.GyroEnabled ?? false : null,
                Rig: presentation?.TransformRig(selectedRig) ?? selectedRig))!.AsObject();
            stored["_sourceFingerprint"] = fingerprint;
            stored["_features"] = JsonSerializer.SerializeToNode(features);
            if (request.Render?.Frame is not null) stored["_deviceVariant"] = true;
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

    public async Task<IReadOnlyList<WallpaperSummary>> ListAsync(Guid? userId, bool shared, CancellationToken ct, ExploreQuery? explore = null)
    {
        if (!shared && !userId.HasValue) throw new UnauthorizedException("로그인이 필요합니다.");
        var query = db.WallpaperAssets.AsNoTracking().Where(x => !x.IsArchived && (shared ? x.IsPublic : x.UserId == userId));
        if (shared && explore is not null)
            query = WallpaperExploreQuery.Filter(query, explore, db.Database.IsRelational());
        var ordered = explore?.Sort switch
        {
            "likes" => query.OrderByDescending(x => x.Likes.Count),
            "downloads" => query.OrderByDescending(x => x.SaveCount),
            "name" => query.OrderBy(x => x.Title),
            _ => query.OrderByDescending(x => x.CreatedAt)
        };
        var assets = await ordered.ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Take(Math.Clamp(explore?.Limit ?? 100, 1, 100))
            .Select(asset => new { Asset = asset, Likes = shared ? asset.Likes.Count : 0,
                Liked = shared && userId.HasValue && asset.Likes.Any(like => like.UserId == userId) }).ToListAsync(ct);
        var authorIds = assets.Select(x => x.Asset.UserId).Distinct().ToArray();
        var authors = await db.Users.AsNoTracking().Where(x => authorIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Username, x.ProfileImageObjectKey }).ToDictionaryAsync(x => x.Id, ct);
        return assets.Select(item => {
            var x = item.Asset;
            var result = shared ? new WallpaperSummary(x.Id, x.Title, x.Mode, x.IsPublic, x.CreatedAt, Edit: EffectiveEdit(x), Features: Features(x)) : Map(x);
            var author = authors.GetValueOrDefault(x.UserId);
            return result with { UserId = x.UserId, Username = author?.Username,
                LikeCount = item.Likes, DownloadCount = x.SaveCount, IsLikedByCurrentUser = item.Liked,
                ProfileImageUrl = author?.ProfileImageObjectKey is { } key ? storage.CreateDownloadUrl(key, UrlLifetime) : null };
        }).ToList();
    }

    public async Task<GalleryEngagement> SetLikeAsync(Guid userId, Guid assetId, bool liked, CancellationToken ct)
    {
        var asset = db.WallpaperAssets.Where(x => x.Id == assetId && x.IsPublic && !x.IsArchived && x.UserId != userId);
        if (!await asset.AnyAsync(ct)) throw new NotFoundException("공유 배경화면을 찾을 수 없습니다.");
        var likes = db.WallpaperLikes.Where(x => x.WallpaperAssetId == assetId && x.UserId == userId);
        if (liked)
        {
            if (!await likes.AnyAsync(ct))
            {
                var entry = db.WallpaperLikes.Add(new WallpaperLike { WallpaperAssetId = assetId, UserId = userId });
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                { entry.State = EntityState.Detached; }
            }
        }
        else if (db.Database.IsRelational()) await likes.ExecuteDeleteAsync(ct);
        else if (await likes.SingleOrDefaultAsync(ct) is { } like)
        {
            db.WallpaperLikes.Remove(like);
            await db.SaveChangesAsync(ct);
        }
        return await asset.Select(x => new GalleryEngagement(x.Likes.Count, x.Likes.Any(l => l.UserId == userId)))
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("공유 배경화면을 찾을 수 없습니다.");
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
        if (isPublic) RequireEditable(asset);
        asset.IsPublic = isPublic;
        await db.SaveChangesAsync(ct);
    }

    public async Task RenameAsync(Guid userId, Guid assetId, RenameWallpaperRequest request, CancellationToken ct)
    {
        var asset = await Owned(userId, assetId, ct);
        RequireEditable(asset);
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 160)
            throw new ValidationException("이미지 이름은 1~160자로 입력해 주세요.");
        if (asset.IsPublic) throw new ConflictException("공유를 끈 뒤 이미지 이름을 변경해 주세요.");
        asset.Title = request.Title.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateMetadataAsync(Guid userId, Guid assetId, UpdateWallpaperMetadataRequest request, CancellationToken ct)
    {
        var asset = await Owned(userId, assetId, ct);
        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160)
            throw new ValidationException("이미지 이름은 1~160자로 입력해 주세요.");
        var edit = RequireEditable(asset) ?? ImageEditState.Original();
        asset.Title = title;
        asset.SavedEditJson = edit.WithTags(request.Tags, request.PlatformTags).ToJson();
        if (asset.SourceProjectId is { } projectId)
        {
            var project = await db.LiveLayerProjects.SingleOrDefaultAsync(x => x.Id == projectId && x.UserId == userId, ct);
            if (project is not null)
            {
                var projectEdit = ImageEditState.RequireEditable(project.SavedEditJson) ?? ImageEditState.Original();
                project.Title = title;
                project.SavedEditJson = projectEdit.WithTags(request.Tags, request.PlatformTags).ToJson();
            }
        }
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
    private async Task CopyLayer(string source, string key, LayerPresentation? presentation, CancellationToken ct, bool fillBackground)
    {
        if (presentation?.ChangesPixels != true) { await copier.CopyAsync(source, key, ct); return; }
        using var input = new MemoryStream(); await storage.DownloadAsync(source, input, ct);
        using var output = new MemoryStream(presentation.Render(input.ToArray(), fillBackground)); await storage.UploadAsync(output, key, "image/png", ct);
    }
    private static WallpaperManifest ReadManifest(WallpaperAsset asset)
    {
        var manifest = JsonSerializer.Deserialize<WallpaperManifest>(asset.ManifestJson)
            ?? throw new InvalidOperationException("저장된 manifest가 올바르지 않습니다.");
        var edit = ImageEditState.FromJson(asset.SavedEditJson);
        if (manifest.Rig is null && edit?.Rig is { } rig) manifest = manifest with { Rig = rig };
        return manifest.MouseEnabled is null && manifest.GyroEnabled is null && edit is { } && (edit.MouseEnabled || edit.GyroEnabled)
            ? manifest with { MouseEnabled = edit.MouseEnabled, GyroEnabled = edit.GyroEnabled } : manifest;
    }
    private static WallpaperSummary Map(WallpaperAsset x)
    {
        using var json = JsonDocument.Parse(x.ManifestJson);
        var deviceVariant = json.RootElement.TryGetProperty("_deviceVariant", out var flag) && flag.ValueKind == JsonValueKind.True;
        return new(x.Id, x.Title, x.Mode, x.IsPublic, x.CreatedAt, x.SourceProjectId,
            EffectiveEdit(x, json.RootElement), Features(x), IsDeviceVariant: deviceVariant);
    }

    private static ImageEditState? RequireEditable(WallpaperAsset asset)
    {
        var edit = EffectiveEdit(asset);
        edit?.EnsureEditable();
        return edit;
    }

    private static ImageEditState? EffectiveEdit(WallpaperAsset asset)
    {
        using var json = JsonDocument.Parse(asset.ManifestJson);
        return EffectiveEdit(asset, json.RootElement);
    }

    private static ImageEditState? EffectiveEdit(WallpaperAsset asset, JsonElement manifest)
    {
        var edit = ImageEditState.FromJson(asset.SavedEditJson);
        if (edit?.IsReadOnly == true) return edit;

        // Older imports owned a copy of the files, but had neither an owned source
        // project nor the creation fingerprint of an authored asset.
        var deviceVariant = manifest.TryGetProperty("_deviceVariant", out var deviceFlag) && deviceFlag.ValueKind == JsonValueKind.True;
        var authored = asset.SourceProjectId.HasValue ||
            (manifest.TryGetProperty("_sourceFingerprint", out var fingerprint) && fingerprint.ValueKind == JsonValueKind.String) ||
            (manifest.TryGetProperty("_ownerSnapshot", out var ownerFlag) && ownerFlag.ValueKind == JsonValueKind.True);
        return deviceVariant || !authored
            ? (edit ?? ImageEditState.Original() with { SavedAt = asset.CreatedAt }) with { IsReadOnly = true }
            : edit;
    }

    private static IReadOnlyList<string> Features(WallpaperAsset asset)
    {
        using var json = JsonDocument.Parse(asset.ManifestJson);
        return json.RootElement.TryGetProperty("_features", out var value)
            ? value.Deserialize<string[]>() ?? []
            : asset.Mode == WallpaperMode.Live2D ? ["배경 분리", "Live 2D"] : [];
    }
}
