using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Projects;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Images;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace UpscaleLab.Infrastructure.Projects;

public sealed class ProjectService(ApplicationDbContext dbContext, IStorageService storageService,
    ILogger<ProjectService>? logger = null) : IProjectService
{
    private static readonly TimeSpan DownloadLifetime = TimeSpan.FromMinutes(15);

    public async Task<ImageContentResponse> ReadContentAsync(Guid userId, Guid projectId, Guid? jobId, Guid? layerId, CancellationToken ct)
    {
        var project = await FindOwnedAsync(userId, projectId, false, ct);
        if (jobId.HasValue && layerId.HasValue) throw new ValidationException("읽을 이미지 종류를 하나만 선택해 주세요.");
        if (layerId.HasValue)
        {
            var layer = project.Layers.SingleOrDefault(x => x.Id == layerId) ?? throw new NotFoundException("레이어를 찾을 수 없습니다.");
            return await ImageContentReader.ReadAsync(storageService, layer.ObjectKey, "image/png", ct);
        }
        if (jobId.HasValue)
        {
            var job = project.ProcessingJobs.SingleOrDefault(x => x.Id == jobId && x.Status == UpscaleLab.Domain.Enums.ProcessingJobStatus.Completed && x.ResultObjectKey != null)
                ?? throw new NotFoundException("완료된 이미지 결과를 찾을 수 없습니다.");
            return await ImageContentReader.ReadAsync(storageService, job.ResultObjectKey!, "image/png", ct);
        }
        return await ImageContentReader.ReadAsync(storageService, project.OriginalImage.OriginalObjectKey, project.OriginalImage.ContentType, ct);
    }

    public async Task<ImageEditState> SaveEditAsync(Guid userId, Guid projectId, SaveImageEditRequest request, CancellationToken ct)
    {
        var project = await dbContext.LiveLayerProjects.SingleOrDefaultAsync(
            x => x.Id == projectId && x.UserId == userId, ct)
            ?? throw new NotFoundException("프로젝트를 찾을 수 없습니다.");
        if (await dbContext.GalleryPosts.AnyAsync(x => x.ProjectId == projectId && x.IsPublic, ct) ||
            await dbContext.WallpaperAssets.AnyAsync(x => x.SourceProjectId == projectId && x.IsPublic && !x.IsArchived, ct))
            throw new ConflictException("공유를 끈 뒤 사진 설정을 변경해 주세요.");
        var previous = ImageEditState.FromJson(project.SavedEditJson);
        var edit = ImageEditState.Create(request, previous);
        if (ProjectDraftState.IsDiscarded(project.SavedEditJson))
            throw new NotFoundException("폐기된 임시 이미지입니다.");
        if ((edit.MouseEnabled || edit.GyroEnabled) && await dbContext.ImageLayers.CountAsync(x => x.ProjectId == projectId, ct) < 2)
            throw new ConflictException("움직임을 사용하려면 배경과 피사체를 먼저 분리해 주세요.");
        if (request.Tags is null) edit = edit with { Tags = previous?.Tags };
        if (request.Title is not null) project.Title = request.Title.Trim();
        project.SavedEditJson = edit.ToJson();
        await dbContext.SaveChangesAsync(ct);
        return edit;
    }

    public async Task RenameAsync(Guid userId, Guid projectId, RenameProjectRequest request, CancellationToken ct)
    {
        var project = await dbContext.LiveLayerProjects.SingleOrDefaultAsync(
            x => x.Id == projectId && x.UserId == userId, ct)
            ?? throw new NotFoundException("프로젝트를 찾을 수 없습니다.");
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 160)
            throw new ValidationException("이미지 이름은 1~160자로 입력해 주세요.");
        if (await dbContext.GalleryPosts.AnyAsync(x => x.ProjectId == projectId && x.IsPublic, ct) ||
            await dbContext.WallpaperAssets.AnyAsync(x => x.SourceProjectId == projectId && x.IsPublic && !x.IsArchived, ct))
            throw new ConflictException("공유를 끈 뒤 이미지 이름을 변경해 주세요.");
        project.Title = request.Title.Trim();
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<ProjectDetailResponse> CreateAsync(
        Guid userId,
        CreateProjectCommand command,
        CancellationToken cancellationToken)
    {
        var projectId = Guid.NewGuid();
        var normalized = await UploadedImage.ReadAsync(command.Content, command.FileName, command.ContentType, cancellationToken);
        await using var pixels = new MemoryStream(normalized.Bytes, false);
        var safeFileName = SanitizeFileName(normalized.FileName);
        var extension = Path.GetExtension(safeFileName);
        var objectKey = $"users/{userId:N}/projects/{projectId:N}/original/{Guid.NewGuid():N}{extension}";
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
            var project = new LiveLayerProject
            {
                Id = projectId,
                UserId = userId,
                OriginalImage = image,
                Title = command.Title.Trim(),
                SavedEditJson = command.IsDraft ? ProjectDraftState.Pending : null
            };

            dbContext.LiveLayerProjects.Add(project);
            await dbContext.SaveChangesAsync(cancellationToken);
            return MapDetail(project);
        }
        catch
        {
            await storageService.DeleteAsync(stored.ObjectKey, CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<ProjectSummaryResponse>> GetAllAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var projects = await dbContext.LiveLayerProjects
            .AsNoTracking()
            .Include(x => x.OriginalImage)
            .Include(x => x.Layers)
            .Include(x => x.ProcessingJobs)
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken);

        return projects.Where(x => !ProjectDraftState.IsDraft(x.SavedEditJson)).Select(MapSummary).ToList();
    }

    public async Task<ProjectDetailResponse> GetAsync(
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await FindOwnedAsync(userId, projectId, false, cancellationToken);
        if (ProjectDraftState.IsDiscarded(project.SavedEditJson)) throw new NotFoundException("프로젝트를 찾을 수 없습니다.");
        return MapDetail(project);
    }

    public async Task<IReadOnlyList<LayerResponse>> GetLayersAsync(
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.LiveLayerProjects.AnyAsync(x => x.Id == projectId && x.UserId == userId, cancellationToken))
        {
            throw new NotFoundException("프로젝트를 찾을 수 없습니다.");
        }

        var layers = await dbContext.ImageLayers
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.LayerOrder)
            .ToListAsync(cancellationToken);
        return layers.Select(MapLayer).ToList();
    }

    public async Task<LayerResponse> UpdateLayerAsync(
        Guid userId,
        Guid projectId,
        Guid layerId,
        UpdateLayerRequest request,
        CancellationToken cancellationToken)
    {
        var layer = await dbContext.ImageLayers
            .Include(x => x.Project)
            .SingleOrDefaultAsync(x => x.Id == layerId && x.ProjectId == projectId, cancellationToken)
            ?? throw new NotFoundException("레이어를 찾을 수 없습니다.");

        if (layer.Project.UserId != userId)
        {
            throw new NotFoundException("레이어를 찾을 수 없습니다.");
        }

        layer.Depth = request.Depth ?? layer.Depth;
        layer.PositionX = request.PositionX ?? layer.PositionX;
        layer.PositionY = request.PositionY ?? layer.PositionY;
        layer.Rotation = request.Rotation ?? layer.Rotation;
        layer.Scale = request.Scale ?? layer.Scale;
        layer.MovementX = request.MovementX ?? layer.MovementX;
        layer.MovementY = request.MovementY ?? layer.MovementY;
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapLayer(layer);
    }

    public async Task<IReadOnlyList<LayerResponse>> ReorderLayersAsync(
        Guid userId, Guid projectId, ReorderProjectLayersRequest request, CancellationToken cancellationToken)
    {
        var project = await dbContext.LiveLayerProjects.Include(x => x.Layers)
            .SingleOrDefaultAsync(x => x.Id == projectId && x.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("프로젝트를 찾을 수 없습니다.");
        if (ProjectDraftState.IsDiscarded(project.SavedEditJson))
            throw new NotFoundException("프로젝트를 찾을 수 없습니다.");
        if (await dbContext.GalleryPosts.AnyAsync(x => x.ProjectId == projectId && x.IsPublic, cancellationToken) ||
            await dbContext.WallpaperAssets.AnyAsync(x => x.SourceProjectId == projectId && x.IsPublic && !x.IsArchived, cancellationToken))
            throw new ConflictException("공유를 끈 뒤 레이어 순서를 편집해 주세요.");
        if (await dbContext.ProcessingJobs.AnyAsync(x => x.ProjectId == projectId &&
                (x.Status == UpscaleLab.Domain.Enums.ProcessingJobStatus.Queued ||
                 x.Status == UpscaleLab.Domain.Enums.ProcessingJobStatus.Processing), cancellationToken))
            throw new ConflictException("이미지 처리가 끝난 다음 레이어 순서를 변경해 주세요.");

        var requested = request.LayerIds;
        var layers = project.Layers.ToList();
        if (requested is null || requested.Count != layers.Count || layers.Count is < 2 or > 64 ||
            requested.Distinct().Count() != layers.Count ||
            !requested.ToHashSet().SetEquals(layers.Select(x => x.Id)))
            throw new ValidationException("모든 레이어를 빠짐없이 한 번씩 선택해 주세요.");

        var backgrounds = layers.Where(x => x.LayerType == UpscaleLab.Domain.Enums.LayerType.Background).ToArray();
        if (backgrounds.Length > 0 && (backgrounds.Length != 1 || requested[0] != backgrounds[0].Id))
            throw new ValidationException("배경 레이어는 항상 가장 아래에 배치해 주세요.");

        var byId = layers.ToDictionary(x => x.Id);
        if (requested.Select((id, order) => byId[id].LayerOrder == order).All(x => x))
            return requested.Select(id => MapLayer(byId[id])).ToList();

        // PostgreSQL has a unique (ProjectId, LayerOrder) index. Direct swaps violate it.
        // First move each order into a temporary negative namespace, then assign 0..N-1.
        // Both saves run inside one transaction: failures cannot leave partial ordering.
        var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            for (var i = 0; i < requested.Count; i++) byId[requested[i]].LayerOrder = -(i + 1);
            await dbContext.SaveChangesAsync(cancellationToken);
            for (var i = 0; i < requested.Count; i++) byId[requested[i]].LayerOrder = i;
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
        return requested.Select(id => MapLayer(byId[id])).ToList();
    }

    public async Task DeleteAsync(Guid userId, Guid projectId, CancellationToken cancellationToken)
    {
        var project = await FindOwnedAsync(userId, projectId, true, cancellationToken);
        var objectKeys = project.Layers.Select(x => x.ObjectKey)
            .Concat(project.ProcessingJobs.Where(x => x.ResultObjectKey != null).Select(x => x.ResultObjectKey!))
            .Concat(project.OriginalImage.OptimizedImages.Select(x => x.ObjectKey))
            .Append(project.OriginalImage.OriginalObjectKey)
            .Append(project.OriginalImage.OriginalObjectKey + ".gallery-480.jpg")
            .Distinct()
            .ToArray();

        dbContext.LiveLayerProjects.Remove(project);
        dbContext.Images.Remove(project.OriginalImage);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var objectKey in objectKeys)
        {
            await storageService.DeleteAsync(objectKey, cancellationToken);
        }
    }

    private async Task<LiveLayerProject> FindOwnedAsync(
        Guid userId,
        Guid projectId,
        bool includeOptimizedImages,
        CancellationToken cancellationToken)
    {
        IQueryable<LiveLayerProject> query = dbContext.LiveLayerProjects
            .Include(x => x.OriginalImage)
            .Include(x => x.Layers)
            .Include(x => x.ProcessingJobs);
        if (includeOptimizedImages)
        {
            query = query.Include(x => x.OriginalImage).ThenInclude(x => x.OptimizedImages);
        }

        return await query.SingleOrDefaultAsync(x => x.Id == projectId && x.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("프로젝트를 찾을 수 없습니다.");
    }

    public Task DiscardDraftAsync(Guid userId, Guid projectId, CancellationToken ct) =>
        DraftProjectCleanup.DiscardAsync(dbContext, storageService, userId, projectId, true, ct);

    public async Task<ProjectDetailResponse> ResetEffectsAsync(Guid userId, Guid projectId, ResetProjectEffectsRequest request, CancellationToken ct)
    {
        var project = await FindOwnedAsync(userId, projectId, false, ct);
        await ProjectEffectResetter.ApplyAsync(dbContext, storageService, project, request.Feature,
            logger ?? NullLogger<ProjectService>.Instance, ct);
        return MapDetail(project);
    }

    private ProjectSummaryResponse MapSummary(LiveLayerProject project) => new(
        project.Id,
        project.Title,
        project.Status,
        storageService.CreateDownloadUrl(project.OriginalImage.OriginalObjectKey, DownloadLifetime),
        project.Layers.Count,
        project.FailureReason,
        project.CreatedAt,
        project.UpdatedAt,
        project.OriginalImage.ContentType,
        ImageEditState.FromJson(project.SavedEditJson),
        project.OriginalImage.OriginalWidth,
        project.OriginalImage.OriginalHeight,
        MapImageResult(project), ProjectFeatures.For(project), ProjectDraftState.IsDraft(project.SavedEditJson));

    private ProjectDetailResponse MapDetail(LiveLayerProject project) => new(
        project.Id,
        project.Title,
        project.Status,
        storageService.CreateDownloadUrl(project.OriginalImage.OriginalObjectKey, DownloadLifetime),
        project.OriginalImage.OriginalWidth,
        project.OriginalImage.OriginalHeight,
        project.FailureReason,
        project.CreatedAt,
        project.UpdatedAt,
        project.Layers.OrderBy(x => x.LayerOrder).Select(MapLayer).ToList(),
        project.ProcessingJobs.OrderByDescending(x => x.CreatedAt).Select(ProjectProcessingService.MapStatus).FirstOrDefault(),
        project.OriginalImage.ContentType,
        ImageEditState.FromJson(project.SavedEditJson),
        MapImageResult(project), ProjectFeatures.For(project), ProjectDraftState.IsDraft(project.SavedEditJson));

    private ProjectImageResult? MapImageResult(LiveLayerProject project)
    {
        var completed = project.ProcessingJobs.Where(x => x.Status == UpscaleLab.Domain.Enums.ProcessingJobStatus.Completed && x.ResultObjectKey != null)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        return completed?.ResultObjectKey is { } key
            ? new(completed.Id, storageService.CreateDownloadUrl(key, DownloadLifetime), completed.ResultWidth, completed.ResultHeight)
            : null;
    }

    private LayerResponse MapLayer(ImageLayer layer) => new(
        layer.Id,
        layer.ProjectId,
        layer.LayerType,
        layer.LayerOrder,
        storageService.CreateDownloadUrl(layer.ObjectKey, DownloadLifetime),
        layer.Depth,
        layer.PositionX,
        layer.PositionY,
        layer.Rotation,
        layer.Scale,
        layer.MovementX,
        layer.MovementY,
        layer.CreatedAt,
        layer.UpdatedAt,
        GetAutomaticLayerOrder(layer));

    // Generated layer object keys are stored as layers/000_<guid>.png. The image key
    // does not change when the user's visible stack order changes, so it retains the
    // original AI ordering without adding a schema migration.
    private static int GetAutomaticLayerOrder(ImageLayer layer)
    {
        var name = Path.GetFileName(layer.ObjectKey) ?? string.Empty;
        return name.Length >= 5 && name[3] == '_' &&
            int.TryParse(name.AsSpan(0, 3), out var order) && order is >= 0 and < 64
            ? order : layer.LayerOrder;
    }

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
