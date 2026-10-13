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
    IProjectImageProcessor imageProcessor,
    IProcessingJobQueue queue,
    ILogger<ProjectProcessingService> logger) : IProjectProcessingService
{
    public async Task<ProcessingStatusResponse> StartAsync(
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken,
        ProcessProjectRequest? request = null)
    {
        request ??= new();
        if (!Enum.IsDefined(request.Operation) || !Enum.IsDefined(request.LayerEngine) || request.UpscaleFactor is not (2 or 4))
            throw new ValidationException("처리 방식과 레이어 엔진, 확대 배율(2 또는 4)을 확인하세요.");
        var project = await dbContext.LiveLayerProjects
            .Include(x => x.ProcessingJobs)
            .SingleOrDefaultAsync(x => x.Id == projectId && x.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("프로젝트를 찾을 수 없습니다.");

        if (project.ProcessingJobs.Any(x => x.Status is ProcessingJobStatus.Queued or ProcessingJobStatus.Processing))
        {
            throw new ConflictException("이미 처리 중인 프로젝트입니다.");
        }

        var job = new ProcessingJob
        {
            ProjectId = project.Id, Operation = request.Operation,
            LayerEngine = request.LayerEngine, UpscaleFactor = request.UpscaleFactor
        };
        project.Status = ProjectStatus.Processing;
        project.FailureReason = null;
        dbContext.ProcessingJobs.Add(job);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            dbContext.ChangeTracker.Clear();
            throw new ConflictException("이미 처리 중인 프로젝트입니다.");
        }
        // Once persisted, a disconnected HTTP caller must not strand the queued job.
        await queue.EnqueueAsync(job.Id, CancellationToken.None);
        return MapStatus(job);
    }

    public async Task<ProcessingDownloadResponse> GetDownloadAsync(
        Guid userId, Guid projectId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await dbContext.ProcessingJobs.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == jobId && x.ProjectId == projectId && x.Project.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("처리 작업을 찾을 수 없습니다.");
        if (job.Status != ProcessingJobStatus.Completed || job.UpscaledObjectKey is null ||
            job.OutputWidth is null || job.OutputHeight is null)
            throw new NotFoundException("완료된 업스케일 이미지가 없습니다.");
        var lifetime = TimeSpan.FromMinutes(15);
        return new(storageService.CreateDownloadUrl(job.UpscaledObjectKey, lifetime),
            DateTime.UtcNow.Add(lifetime), job.OutputWidth.Value, job.OutputHeight.Value);
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
            job.Progress = 0;
        }

        if (jobs.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            foreach (var job in jobs)
                await queue.EnqueueAsync(job.Id, cancellationToken);
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
            var result = await imageProcessor.ProcessAsync(
                new ProjectProcessingRequest(inputPath, outputPath, job.Operation, job.LayerEngine, job.UpscaleFactor),
                cancellationToken);
            var replaceLayers = job.Operation != ProcessingOperation.Upscale;
            if (replaceLayers && result.Layers.Count == 0)
            {
                throw new InvalidOperationException("처리 엔진이 레이어를 생성하지 않았습니다.");
            }

            string? upscaledKey = null;
            if (job.Operation != ProcessingOperation.Layers)
            {
                if (result.UpscaledFilePath is null || result.Width <= 0 || result.Height <= 0)
                    throw new InvalidOperationException("업스케일 이미지 결과가 없습니다.");
                var key = $"users/{job.Project.UserId:N}/projects/{job.ProjectId:N}/upscaled/{job.Id:N}_{Guid.NewGuid():N}.png";
                await using var image = File.OpenRead(result.UpscaledFilePath);
                var stored = await storageService.UploadAsync(image, key, "image/png", cancellationToken);
                uploadedKeys.Add(stored.ObjectKey);
                upscaledKey = stored.ObjectKey;
            }

            job.Progress = 80;
            await dbContext.SaveChangesAsync(cancellationToken);
            var newLayers = new List<ImageLayer>();
            foreach (var layer in (replaceLayers ? result.Layers : []).OrderBy(x => x.LayerOrder))
            {
                var objectKey = $"users/{job.Project.UserId:N}/projects/{job.ProjectId:N}/layers/{layer.LayerOrder:D3}_{Guid.NewGuid():N}.png";
                await using var stream = File.OpenRead(layer.FilePath);
                var stored = await storageService.UploadAsync(stream, objectKey, "image/png", cancellationToken);
                uploadedKeys.Add(stored.ObjectKey);
                newLayers.Add(new ImageLayer
                {
                    ProjectId = job.ProjectId,
                    LayerType = layer.LayerType,
                    LayerOrder = layer.LayerOrder,
                    ImageUrl = stored.StorageUrl,
                    ObjectKey = stored.ObjectKey,
                    Depth = layer.Depth,
                    PositionX = layer.PositionX,
                    PositionY = layer.PositionY,
                    Rotation = layer.Rotation,
                    Scale = layer.Scale,
                    MovementX = layer.MovementX,
                    MovementY = layer.MovementY
                });
            }

            var oldKeys = replaceLayers ? job.Project.Layers.Select(x => x.ObjectKey).ToArray() : [];
            if (replaceLayers)
            {
                dbContext.ImageLayers.RemoveRange(job.Project.Layers);
                dbContext.ImageLayers.AddRange(newLayers);
            }
            job.Status = ProcessingJobStatus.Completed;
            job.UpscaledObjectKey = upscaledKey;
            job.OutputWidth = upscaledKey is null ? null : result.Width;
            job.OutputHeight = upscaledKey is null ? null : result.Height;
            job.Progress = 100;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = null;
            job.Project.Status = ProjectStatus.Completed;
            job.Project.FailureReason = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            uploadedKeys.Clear();

            foreach (var oldKey in oldKeys)
            {
                await TryDeleteAsync(oldKey);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            foreach (var key in uploadedKeys) await TryDeleteAsync(key);
            await RecordFailureAsync(jobId, null, true);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "LiveLayer processing job {JobId} failed", jobId);
            foreach (var uploadedKey in uploadedKeys)
            {
                await TryDeleteAsync(uploadedKey);
            }

            var message = exception is ConfigurationException or TimeoutException
                ? Truncate(exception.Message, 2000)
                : "이미지 처리에 실패했습니다. GPU 서버 상태와 API 로그를 확인한 후 다시 시도하세요.";
            await RecordFailureAsync(jobId, message, false);
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
        job.UpdatedAt,
        job.Operation, job.LayerEngine, job.UpscaleFactor,
        job.Status == ProcessingJobStatus.Completed && job.UpscaledObjectKey != null,
        job.OutputWidth, job.OutputHeight);

    private async Task RecordFailureAsync(Guid jobId, string? message, bool requeue)
    {
        // Discard pending artifact additions/deletions before recording a failed attempt.
        dbContext.ChangeTracker.Clear();
        var saved = await dbContext.ProcessingJobs.Include(x => x.Project)
            .SingleOrDefaultAsync(x => x.Id == jobId, CancellationToken.None);
        if (saved is null || saved.Status == ProcessingJobStatus.Completed) return;
        saved.Status = requeue ? ProcessingJobStatus.Queued : ProcessingJobStatus.Failed;
        saved.Progress = 0;
        saved.ErrorMessage = message;
        saved.CompletedAt = requeue ? null : DateTime.UtcNow;
        saved.Project.Status = requeue ? ProjectStatus.Processing : ProjectStatus.Failed;
        saved.Project.FailureReason = message;
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

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
