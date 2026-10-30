using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Projects;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Projects;

internal static class ProjectEffectResetter
{
    public static async Task ApplyAsync(ApplicationDbContext db, IStorageService storage, LiveLayerProject project,
        string feature, ILogger logger, CancellationToken ct)
    {
        if (feature is not ("All" or "Upscale" or "Layers"))
            throw new ValidationException("초기화할 편집 기능을 확인해 주세요.");
        if (ProjectDraftState.IsDiscarded(project.SavedEditJson)) throw new NotFoundException("폐기된 임시 이미지입니다.");
        if (project.ProcessingJobs.Any(x => x.Status is ProcessingJobStatus.Queued or ProcessingJobStatus.Processing))
            throw new ConflictException("이미지 처리가 완료된 뒤 적용을 취소해 주세요.");
        if (await db.GalleryPosts.AnyAsync(x => x.ProjectId == project.Id && x.IsPublic, ct) ||
            await db.WallpaperAssets.AnyAsync(x => x.SourceProjectId == project.Id && x.IsPublic && !x.IsArchived, ct))
            throw new ConflictException("공유를 끈 뒤 이미지 편집을 초기화해 주세요.");

        var upscale = feature is "All" or "Upscale";
        var layers = feature is "All" or "Layers";
        var obsolete = new HashSet<string>();
        var uploaded = new List<string>();
        var committed = false;
        try
        {
            // Layer files may have been enlarged along with the upscaled image.
            // Keep separation when only upscale is removed, using the original canvas and alpha.
            if (upscale && !layers && project.ProcessingJobs.Any(x => x.ResultObjectKey != null))
            {
                foreach (var layer in project.Layers)
                {
                    await using var input = new MemoryStream();
                    await storage.DownloadAsync(layer.ObjectKey, input, ct); input.Position = 0;
                    using var pixels = new MagickImage(input);
                    var rx = project.OriginalImage.OriginalWidth / (double)pixels.Width;
                    var ry = project.OriginalImage.OriginalHeight / (double)pixels.Height;
                    if (rx == 1 && ry == 1) continue;
                    pixels.FilterType = FilterType.Lanczos;
                    pixels.Resize(new MagickGeometry((uint)project.OriginalImage.OriginalWidth, (uint)project.OriginalImage.OriginalHeight) { IgnoreAspectRatio = true });
                    await using var output = new MemoryStream(pixels.ToByteArray(MagickFormat.Png), false);
                    var key = $"users/{project.UserId:N}/projects/{project.Id:N}/layers/{layer.LayerOrder:D3}_{Guid.NewGuid():N}.png";
                    var stored = await storage.UploadAsync(output, key, "image/png", ct);
                    uploaded.Add(key); obsolete.Add(layer.ObjectKey);
                    layer.ObjectKey = stored.ObjectKey; layer.ImageUrl = stored.StorageUrl;
                    layer.PositionX *= rx; layer.PositionY *= ry;
                    layer.MovementX *= rx; layer.MovementY *= ry;
                }
            }
            if (layers)
            {
                foreach (var layer in project.Layers.ToArray())
                {
                    obsolete.Add(layer.ObjectKey); db.ImageLayers.Remove(layer); project.Layers.Remove(layer);
                }
            }
            foreach (var job in project.ProcessingJobs.ToArray())
            {
                if (upscale && job.ResultObjectKey is { } key) { obsolete.Add(key); job.ResultObjectKey = null; }
                var remove = feature == "All" ||
                    (feature == "Upscale" && job.Operation == ProcessingOperation.Upscale) ||
                    (feature == "Layers" && job.Operation == ProcessingOperation.Layers);
                if (remove) {
                    if (job.SubjectMaskObjectKey is { } maskKey) obsolete.Add(maskKey);
                    db.ProcessingJobs.Remove(job); project.ProcessingJobs.Remove(job); continue;
                }
                if (job.Operation == ProcessingOperation.UpscaleAndLayers)
                    job.Operation = layers ? ProcessingOperation.Upscale : ProcessingOperation.Layers;
                if (upscale)
                {
                    job.ResultWidth = project.OriginalImage.OriginalWidth;
                    job.ResultHeight = project.OriginalImage.OriginalHeight;
                }
            }
            var edit = ImageEditState.FromJson(project.SavedEditJson);
            // Reset never promotes a new, unsaved upload into the library.
            if (edit is not null)
                project.SavedEditJson = (feature == "All"
                    ? new ImageEditState(70, "original", "original", 1, true, DateTime.UtcNow, edit.Tags)
                    : edit with { Scale = upscale ? 1 : edit.Scale,
                        MouseEnabled = !layers && edit.MouseEnabled, GyroEnabled = !layers && edit.GyroEnabled,
                        Foreground = layers || edit.Foreground, SavedAt = DateTime.UtcNow }).ToJson();
            project.Status = project.Layers.Count > 0 || project.ProcessingJobs.Any(x => x.ResultObjectKey != null)
                ? ProjectStatus.Completed : ProjectStatus.Uploaded;
            project.FailureReason = null;
            await db.SaveChangesAsync(ct); committed = true;
        }
        finally
        {
            foreach (var key in committed ? obsolete.AsEnumerable() : uploaded)
                try { await storage.DeleteAsync(key, CancellationToken.None); }
                catch (Exception error) { logger.LogWarning(error, "Failed to remove reset result {ObjectKey}", key); }
        }
    }
}
