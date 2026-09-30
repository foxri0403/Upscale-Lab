using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Projects;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Projects;

public sealed class ProjectService(ApplicationDbContext dbContext, IStorageService storageService) : IProjectService
{
    private static readonly TimeSpan DownloadLifetime = TimeSpan.FromMinutes(15);

    public async Task<ProjectDetailResponse> CreateAsync(
        Guid userId,
        CreateProjectCommand command,
        CancellationToken cancellationToken)
    {
        var projectId = Guid.NewGuid();
        var safeFileName = SanitizeFileName(command.FileName);
        var extension = Path.GetExtension(safeFileName);
        var objectKey = $"users/{userId:N}/projects/{projectId:N}/original/{Guid.NewGuid():N}{extension}";
        var stored = await storageService.UploadAsync(command.Content, objectKey, command.ContentType, cancellationToken);

        try
        {
            var image = new Image
            {
                UserId = userId,
                OriginalUrl = stored.StorageUrl,
                OriginalObjectKey = stored.ObjectKey,
                OriginalWidth = command.OriginalWidth,
                OriginalHeight = command.OriginalHeight,
                FileName = safeFileName,
                ContentType = command.ContentType
            };
            var project = new LiveLayerProject
            {
                Id = projectId,
                UserId = userId,
                OriginalImage = image,
                Title = command.Title.Trim()
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
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken);

        return projects.Select(MapSummary).ToList();
    }

    public async Task<ProjectDetailResponse> GetAsync(
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await FindOwnedAsync(userId, projectId, false, cancellationToken);
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

    public async Task DeleteAsync(Guid userId, Guid projectId, CancellationToken cancellationToken)
    {
        var project = await FindOwnedAsync(userId, projectId, true, cancellationToken);
        var objectKeys = project.Layers.Select(x => x.ObjectKey)
            .Concat(project.OriginalImage.OptimizedImages.Select(x => x.ObjectKey))
            .Append(project.OriginalImage.OriginalObjectKey)
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

    private ProjectSummaryResponse MapSummary(LiveLayerProject project) => new(
        project.Id,
        project.Title,
        project.Status,
        storageService.CreateDownloadUrl(project.OriginalImage.OriginalObjectKey, DownloadLifetime),
        project.Layers.Count,
        project.FailureReason,
        project.CreatedAt,
        project.UpdatedAt);

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
        project.ProcessingJobs.OrderByDescending(x => x.CreatedAt).Select(ProjectProcessingService.MapStatus).FirstOrDefault());

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
        layer.UpdatedAt);

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
