using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Projects;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Projects;

internal static class DraftProjectCleanup
{
    public static async Task DiscardAsync(ApplicationDbContext db, IStorageService storage, Guid userId,
        Guid projectId, bool requested, CancellationToken ct)
    {
        var current = await db.LiveLayerProjects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId, ct);
        if (current is null) return;
        if (current.UserId != userId) throw new NotFoundException("프로젝트를 찾을 수 없습니다.");
        if (!ProjectDraftState.IsDraft(current.SavedEditJson) || (!requested && !ProjectDraftState.IsDiscarded(current.SavedEditJson))) return;
        var project = await db.LiveLayerProjects.Include(x => x.OriginalImage).ThenInclude(x => x.OptimizedImages)
            .Include(x => x.Layers).Include(x => x.ProcessingJobs).SingleAsync(x => x.Id == projectId && x.UserId == userId, ct);
        if (project.ProcessingJobs.Any(x => x.Status is ProcessingJobStatus.Queued or ProcessingJobStatus.Processing))
        {
            if (requested) { project.SavedEditJson = ProjectDraftState.Discarded; await db.SaveChangesAsync(ct); }
            return;
        }
        var keys = project.Layers.Select(x => x.ObjectKey)
            .Concat(project.ProcessingJobs.Where(x => x.ResultObjectKey != null).Select(x => x.ResultObjectKey!))
            .Concat(project.OriginalImage.OptimizedImages.Select(x => x.ObjectKey))
            .Append(project.OriginalImage.OriginalObjectKey).Append(project.OriginalImage.OriginalObjectKey + ".gallery-480.jpg").Distinct().ToArray();
        db.LiveLayerProjects.Remove(project); db.Images.Remove(project.OriginalImage); await db.SaveChangesAsync(ct);
        foreach (var key in keys) await storage.DeleteAsync(key, ct);
    }
}
