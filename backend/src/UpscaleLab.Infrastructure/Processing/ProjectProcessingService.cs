using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Projects;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Processing;

public sealed class ProjectProcessingService(
    ApplicationDbContext dbContext,
    IStorageService storageService,
    IImageLayerProcessor layerProcessor,
    IProcessingJobQueue queue,
    ILogger<ProjectProcessingService> logger) : IProjectProcessingService
{
    public async Task<ProcessingStatusResponse> StartAsync(
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.LiveLayerProjects
            .Include(x => x.ProcessingJobs)
            .SingleOrDefaultAsync(x => x.Id == projectId && x.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("프로젝트를 찾을 수 없습니다.");

        if (project.ProcessingJobs.Any(x => x.Status is ProcessingJobStatus.Queued or ProcessingJobStatus.Processing))
        {
            throw new ConflictException("이미 처리 중인 프로젝트입니다.");
        }

        var job = new ProcessingJob { ProjectId = project.Id };
        project.Status = ProjectStatus.Processing;
        project.FailureReason = null;
        dbContext.ProcessingJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        await queue.EnqueueAsync(job.Id, cancellationToken);
        return MapStatus(job);
    }

    public async Task<ProcessingStatusResponse> GetStatusAsync(
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.LiveLayerProjects.AnyAsync(x => x.Id == projectId && x.UserId == userId, cancellationToken))
        {
            throw new NotFoundException("프로젝트를 찾을 수 없습니다.");
        }

        var job = await dbContext.ProcessingJobs
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("처리 작업을 찾을 수 없습니다.");
        return MapStatus(job);
    }

    public async Task RecoverPendingAsync(CancellationToken cancellationToken)
    {
        var jobs = await dbContext.ProcessingJobs
            .Where(x => x.Status == ProcessingJobStatus.Queued || x.Status == ProcessingJobStatus.Processing)
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            job.Status = ProcessingJobStatus.Queued;
            job.ErrorMessage = null;
            await queue.EnqueueAsync(job.Id, cancellationToken);
        }

        if (jobs.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await dbContext.ProcessingJobs
            .Include(x => x.Project).ThenInclude(x => x.OriginalImage)
            .Include(x => x.Project).ThenInclude(x => x.Layers)
            .SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        if (job is null || job.Status is ProcessingJobStatus.Completed or ProcessingJobStatus.Failed)
        {
            return;
        }

        var workspace = Path.Combine(Path.GetTempPath(), "live-layer", job.Id.ToString("N"));
        var inputPath = Path.Combine(workspace, $"input{Path.GetExtension(job.Project.OriginalImage.FileName)}");
        var outputPath = Path.Combine(workspace, "output");
        var uploadedKeys = new List<string>();

        try
        {
            Directory.CreateDirectory(outputPath);
            job.Status = ProcessingJobStatus.Processing;
            job.Progress = 5;
            job.StartedAt ??= DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            await using (var input = File.Create(inputPath))
            {
                await storageService.DownloadAsync(job.Project.OriginalImage.OriginalObjectKey, input, cancellationToken);
            }

            job.Progress = 15;
            await dbContext.SaveChangesAsync(cancellationToken);
            var processed = await layerProcessor.ProcessAsync(
                new LayerProcessingRequest(inputPath, outputPath),
                cancellationToken);
            if (processed.Count == 0)
            {
                throw new InvalidOperationException("See-through가 레이어를 생성하지 않았습니다.");
            }

            job.Progress = 80;
            await dbContext.SaveChangesAsync(cancellationToken);
            var newLayers = new List<ImageLayer>();
            foreach (var result in processed.OrderBy(x => x.LayerOrder))
            {
                var objectKey = $"users/{job.Project.UserId:N}/projects/{job.ProjectId:N}/layers/{result.LayerOrder:D3}_{Guid.NewGuid():N}.png";
                await using var stream = File.OpenRead(result.FilePath);
                var stored = await storageService.UploadAsync(stream, objectKey, "image/png", cancellationToken);
                uploadedKeys.Add(stored.ObjectKey);
                newLayers.Add(new ImageLayer
                {
                    ProjectId = job.ProjectId,
                    LayerType = result.LayerType,
                    LayerOrder = result.LayerOrder,
                    ImageUrl = stored.StorageUrl,
                    ObjectKey = stored.ObjectKey,
                    Depth = result.Depth,
                    PositionX = result.PositionX,
                    PositionY = result.PositionY,
                    Rotation = result.Rotation,
                    Scale = result.Scale,
                    MovementX = result.MovementX,
                    MovementY = result.MovementY
                });
            }

            var oldKeys = job.Project.Layers.Select(x => x.ObjectKey).ToArray();
            dbContext.ImageLayers.RemoveRange(job.Project.Layers);
            dbContext.ImageLayers.AddRange(newLayers);
            job.Status = ProcessingJobStatus.Completed;
            job.Progress = 100;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = null;
            job.Project.Status = ProjectStatus.Completed;
            job.Project.FailureReason = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var oldKey in oldKeys)
            {
                await TryDeleteAsync(oldKey);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Status = ProcessingJobStatus.Queued;
            job.ErrorMessage = null;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "LiveLayer processing job {JobId} failed", jobId);
            foreach (var uploadedKey in uploadedKeys)
            {
                await TryDeleteAsync(uploadedKey);
            }

            var message = Truncate(exception.Message, 2000);
            job.Status = ProcessingJobStatus.Failed;
            job.ErrorMessage = message;
            job.CompletedAt = DateTime.UtcNow;
            job.Project.Status = ProjectStatus.Failed;
            job.Project.FailureReason = message;
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            try
            {
                if (Directory.Exists(workspace))
                {
                    Directory.Delete(workspace, true);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to clean processing workspace {Workspace}", workspace);
            }
        }
    }

    public static ProcessingStatusResponse MapStatus(ProcessingJob job) => new(
        job.Id,
        job.ProjectId,
        job.Status,
        job.Progress,
        job.ErrorMessage,
        job.StartedAt,
        job.CompletedAt,
        job.UpdatedAt);

    private async Task TryDeleteAsync(string objectKey)
    {
        try
        {
            await storageService.DeleteAsync(objectKey, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to delete obsolete S3 object {ObjectKey}", objectKey);
        }
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
