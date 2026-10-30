using Microsoft.EntityFrameworkCore;
using ImageMagick;
using Microsoft.Extensions.Logging;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Projects;
using UpscaleLab.Application.Storage;
using UpscaleLab.Application.Upscaling;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Tokens;

namespace UpscaleLab.Infrastructure.Processing;

public sealed class ProjectProcessingService(
    ApplicationDbContext dbContext,
    IStorageService storageService,
    IProjectImageProcessor processor,
    IProcessingJobQueue queue,
    ITokenService tokenService,
    ILogger<ProjectProcessingService> logger) : IProjectProcessingService
{
    public Task<ProcessingStatusResponse> StartAsync(Guid userId, Guid projectId, CancellationToken cancellationToken) =>
        StartAsync(userId, projectId, new ProcessProjectRequest(), cancellationToken);

    public async Task<ProcessingStatusResponse> StartAsync(Guid userId, Guid projectId,
        ProcessProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await dbContext.LiveLayerProjects
            .Include(x => x.ProcessingJobs)
            .Include(x => x.OriginalImage)
            .SingleOrDefaultAsync(x => x.Id == projectId && x.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("프로젝트를 찾을 수 없습니다.");

        if (!Enum.IsDefined(request.Operation) || request.UpscaleFactor is not (2 or 4))
            throw new ValidationException("지원하는 작업 종류와 배율(2 또는 4)을 선택하세요.");
        if (request.TargetLongEdge is int edge)
        {
            if (request.Operation != ProcessingOperation.Upscale || !UpscaleResolution.IsSupported(edge))
                throw new ValidationException("FHD, QHD, 4K 중 업스케일 목표 해상도를 선택해 주세요.");
            if (Math.Max(project.OriginalImage.OriginalWidth, project.OriginalImage.OriginalHeight) >= edge)
                throw new ValidationException("현재 이미지보다 높은 해상도를 선택해 주세요.");
            if (project.ProcessingJobs.Any(x => x.Operation == ProcessingOperation.Upscale &&
                x.Status == ProcessingJobStatus.Completed && x.ResultObjectKey != null &&
                Math.Max(x.ResultWidth, x.ResultHeight) >= edge))
                throw new ConflictException("선택한 해상도 이상의 업스케일 결과가 이미 있습니다.");
        }
        if (!processor.Supports(request.Operation))
            throw new ConfigurationException(request.Operation == ProcessingOperation.Layers
                ? "배경 분리 공급자가 아직 연결되지 않았습니다." : "클라우드 업스케일링 공급자가 아직 연결되지 않았습니다.");
        if (await dbContext.GalleryPosts.AnyAsync(x => x.ProjectId == projectId && x.IsPublic, cancellationToken) ||
            await dbContext.WallpaperAssets.AnyAsync(x => x.SourceProjectId == projectId && x.IsPublic && !x.IsArchived, cancellationToken))
            throw new ConflictException("업로드를 해제한 뒤 이미지를 변경해 주세요.");

        if (project.ProcessingJobs.Any(x => x.Status is ProcessingJobStatus.Queued or ProcessingJobStatus.Processing))
        {
            throw new ConflictException("이미 처리 중인 프로젝트입니다.");
        }

        var job = new ProcessingJob { ProjectId = project.Id, Operation = request.Operation,
            TargetLongEdge = request.TargetLongEdge,
            UpscaleFactor = request.TargetLongEdge is int target
                ? UpscaleResolution.NativeScale(project.OriginalImage.OriginalWidth, project.OriginalImage.OriginalHeight, target)
                : request.UpscaleFactor };
        project.Status = ProjectStatus.Processing;
        project.FailureReason = null;
        // 작업 생성과 토큰 예약을 하나의 트랜잭션으로 처리
        await using (var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            dbContext.ProcessingJobs.Add(job);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (request.Operation == ProcessingOperation.Upscale)
            {
                if (request.TargetLongEdge is not int targetEdge)
                    throw new ValidationException(
                        "업스케일링 목표 해상도(FHD/QHD/4K)가 필요합니다.");

                await tokenService.ReserveAsync(
                    userId, job.Id, targetEdge, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        await queue.EnqueueAsync(job.Id, cancellationToken);
        return MapStatus(job);
    }

    public async Task<UpscaleLab.Application.Images.DownloadUrlResponse> GetResultUrlAsync(
        Guid userId, Guid projectId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await dbContext.ProcessingJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId &&
            x.ProjectId == projectId && x.Project.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("처리 작업을 찾을 수 없습니다.");
        if (job.Status != ProcessingJobStatus.Completed || job.ResultObjectKey is null)
            throw new ConflictException("다운로드할 완료 이미지가 없습니다.");
        return new(storageService.CreateDownloadUrl(job.ResultObjectKey, TimeSpan.FromMinutes(15)), DateTime.UtcNow.AddMinutes(15));
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
            .Include(x => x.Project).ThenInclude(x => x.ProcessingJobs)
            .SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        if (job is null || job.Status is ProcessingJobStatus.Completed or ProcessingJobStatus.Failed)
        {
            return;
        }

        var ownerId = job.Project.UserId;
        if (ProjectDraftState.IsDiscarded(job.Project.SavedEditJson))
        {
            job.Status = ProcessingJobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;

            await using (var transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                if (job.Operation == ProcessingOperation.Upscale)
                    await tokenService.RefundAsync(job.Id, cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
            await Projects.DraftProjectCleanup.DiscardAsync(dbContext, storageService, ownerId, job.ProjectId, false, cancellationToken);
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

            // Separation uses the latest successful image, even if a subsequent job failed.
            var latestImage = job.Project.ProcessingJobs.Where(x => x.Status == ProcessingJobStatus.Completed && x.ResultObjectKey != null)
                .OrderByDescending(x => x.CreatedAt).FirstOrDefault();
            var inputKey = job.Operation == ProcessingOperation.Layers && latestImage != null
                ? latestImage.ResultObjectKey! : job.Project.OriginalImage.OriginalObjectKey;
            await using (var input = File.Create(inputPath))
            {
                await storageService.DownloadAsync(inputKey, input, cancellationToken);
            }

            job.Progress = 15;
            if (job.TargetLongEdge is int requestedEdge)
            {
                var originalInfo = new MagickImageInfo(inputPath);
                if (Math.Max(originalInfo.Width, originalInfo.Height) >= requestedEdge)
                    throw new InvalidDataException("현재 이미지보다 높은 해상도를 선택해 주세요.");
                job.UpscaleFactor = UpscaleResolution.NativeScale((int)originalInfo.Width, (int)originalInfo.Height, requestedEdge);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            var processed = await processor.ProcessAsync(
                new ProjectProcessingRequest(inputPath, outputPath, job.Operation, job.UpscaleFactor, job.Id,
                    job.TargetLongEdge, async (progress, ct) => {
                        job.Progress = Math.Clamp(progress, 15, 75);
                        await dbContext.SaveChangesAsync(ct);
                    }),
                cancellationToken);
            var hasLayers = job.Operation != ProcessingOperation.Upscale;
            var hasImage = job.Operation != ProcessingOperation.Layers;
            string? imageResultKey = null;
            if (processed.Width < 1 || processed.Height < 1 || processed.Width > 32768 || processed.Height > 32768 ||
                (long)processed.Width * processed.Height > 64_000_000 || processed.Layers.Count > 64 ||
                (hasLayers ? processed.Layers.Count == 0 : processed.Layers.Count != 0) ||
                hasImage != (processed.ImagePath != null) || processed.Layers.Select(x => x.LayerOrder).Distinct().Count() != processed.Layers.Count)
            {
                throw new InvalidDataException("처리 결과 규격이 일치하지 않습니다.");
            }
            foreach (var layer in processed.Layers)
            {
                if (!Enum.IsDefined(layer.LayerType) || layer.LayerOrder is < 0 or >= 64 ||
                    !Valid(layer.Depth, -100, 100) || !Valid(layer.PositionX, -100000, 100000) ||
                    !Valid(layer.PositionY, -100000, 100000) || !Valid(layer.Rotation, -360, 360) ||
                    !Valid(layer.Scale, 0.01, 100) || !Valid(layer.MovementX, 0, 10000) || !Valid(layer.MovementY, 0, 10000))
                    throw new InvalidDataException("레이어 변환 값이 올바르지 않습니다.");
                ValidatePng(layer.FilePath, outputPath, processed.Width, processed.Height);
            }
            if (processed.ImagePath != null)
            {
                ValidatePng(processed.ImagePath, outputPath, processed.Width, processed.Height);
                if (job.TargetLongEdge is int target)
                {
                    using var original = new MagickImage(inputPath);
                    original.AutoOrient();
                    var expected = UpscaleResolution.Dimensions((int)original.Width, (int)original.Height, target);
                    if (processed.Width != expected.Width || processed.Height != expected.Height)
                        throw new InvalidDataException("업스케일 결과가 선택한 해상도와 일치하지 않습니다.");
                }
                else
                {
                    var inputInfo = new MagickImageInfo(inputPath);
                    if (processed.Width != (long)inputInfo.Width * job.UpscaleFactor || processed.Height != (long)inputInfo.Height * job.UpscaleFactor)
                        throw new InvalidDataException("업스케일 결과 해상도가 배율과 일치하지 않습니다.");
                }
                var key = $"users/{job.Project.UserId:N}/projects/{job.ProjectId:N}/results/{job.Id:N}/result.png";
                await using var resultStream = File.OpenRead(processed.ImagePath);
                await storageService.UploadAsync(resultStream, key, "image/png", cancellationToken);
                uploadedKeys.Add(key);
                imageResultKey = key;
            }

            // Preserve separation and animation when the image canvas is enlarged.
            // GPU inference supplies the full image; transparent layer geometry is resampled without flattening it.
            if (job.Operation == ProcessingOperation.Upscale && job.Project.Layers.Count >= 2)
            {
                var resized = new List<ProcessedLayer>();
                foreach (var existing in job.Project.Layers.OrderBy(x => x.LayerOrder))
                {
                    var path = Path.Combine(outputPath, $"preserved-{existing.Id:N}.png");
                    await using var source = new MemoryStream();
                    await storageService.DownloadAsync(existing.ObjectKey, source, cancellationToken);
                    source.Position = 0;
                    using var image = new MagickImage(source);
                    var ratioX = processed.Width / (double)image.Width;
                    var ratioY = processed.Height / (double)image.Height;
                    image.FilterType = FilterType.Lanczos;
                    image.Resize(new MagickGeometry((uint)processed.Width, (uint)processed.Height) { IgnoreAspectRatio = true });
                    image.Write(path, MagickFormat.Png);
                    resized.Add(new(path, existing.LayerType, existing.LayerOrder, existing.Depth,
                        Math.Clamp(existing.PositionX * ratioX, -100000, 100000), Math.Clamp(existing.PositionY * ratioY, -100000, 100000), existing.Rotation, existing.Scale,
                        Math.Clamp(existing.MovementX * ratioX, 0, 10000), Math.Clamp(existing.MovementY * ratioY, 0, 10000)));
                }
                processed = processed with { Layers = resized };
                hasLayers = true;
            }

            job.Progress = 80;
            await dbContext.SaveChangesAsync(cancellationToken);
            var newLayers = new List<ImageLayer>();
            foreach (var result in processed.Layers.OrderBy(x => x.LayerOrder))
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

            var oldKeys = hasLayers ? job.Project.Layers.Select(x => x.ObjectKey).ToArray() : [];
            if (hasLayers)
            {
                dbContext.ImageLayers.RemoveRange(job.Project.Layers);
                dbContext.ImageLayers.AddRange(newLayers);
            }
            job.ResultObjectKey = imageResultKey;
            job.ResultWidth = processed.Width; job.ResultHeight = processed.Height;
            job.Status = ProcessingJobStatus.Completed;
            job.Progress = 100;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = null;
            job.Project.Status = ProjectStatus.Completed;
            job.Project.FailureReason = null;

            await using (var transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                if (job.Operation == ProcessingOperation.Upscale)
                    await tokenService.CompleteAsync(job.Id, cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }

            foreach (var oldKey in oldKeys)
            {
                await TryDeleteAsync(oldKey);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            foreach (var uploadedKey in uploadedKeys) await TryDeleteAsync(uploadedKey);
            dbContext.ChangeTracker.Clear();
            job = await dbContext.ProcessingJobs.SingleAsync(x => x.Id == jobId, CancellationToken.None);
            job.Status = ProcessingJobStatus.Queued;
            job.ErrorMessage = null;
            job.ResultObjectKey = null; job.ResultWidth = 0; job.ResultHeight = 0;
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
            dbContext.ChangeTracker.Clear();
            job = await dbContext.ProcessingJobs.Include(x => x.Project).SingleAsync(x => x.Id == jobId, CancellationToken.None);
            var message = $"이미지 처리에 실패했습니다. 작업 ID: {jobId}";
            job.Status = ProcessingJobStatus.Failed;
            job.ResultObjectKey = null; job.ResultWidth = 0; job.ResultHeight = 0;
            job.ErrorMessage = message;
            job.CompletedAt = DateTime.UtcNow;
            job.Project.Status = ProjectStatus.Failed;
            job.Project.FailureReason = message;

            await using (var transaction =
                await dbContext.Database.BeginTransactionAsync(CancellationToken.None))
            {
                await dbContext.SaveChangesAsync(CancellationToken.None);

                if (job.Operation == ProcessingOperation.Upscale)
                    await tokenService.RefundAsync(job.Id, CancellationToken.None);

                await transaction.CommitAsync(CancellationToken.None);
            }
        }
        finally
        {
            try
            {
                await Projects.DraftProjectCleanup.DiscardAsync(dbContext, storageService, ownerId, job.ProjectId, false, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to discard temporary project {ProjectId}", job.ProjectId);
            }
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
        job.UpdatedAt, job.Operation, job.UpscaleFactor, job.ResultObjectKey != null, job.ResultWidth, job.ResultHeight,
        job.TargetLongEdge);

    private static void ValidatePng(string filePath, string outputPath, int width, int height)
    {
        if (!Path.GetFullPath(filePath).StartsWith(Path.GetFullPath(outputPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("처리 결과가 작업 디렉터리 밖에 있습니다.");
        var info = new MagickImageInfo(filePath);
        if (info.Format != MagickFormat.Png || info.Width != width || info.Height != height)
            throw new InvalidDataException("PNG 형식 또는 캔버스 크기가 일치하지 않습니다.");
    }

    private static bool Valid(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;

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

}
