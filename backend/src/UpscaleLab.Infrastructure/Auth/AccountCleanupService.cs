using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Auth;

public sealed class AccountCleanupService(ApplicationDbContext db, IStorageService storage,
    IAccountIdentityRemover identity, TimeProvider? clock = null, ILogger<AccountCleanupService>? logger = null) : IAccountCleanupService
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    public async Task ProcessPendingAsync(CancellationToken ct)
    {
        var ids = await db.AccountCleanupTasks.AsNoTracking().Where(x => x.NextAttemptAt <= Now)
            .OrderBy(x => x.NextAttemptAt).Take(10).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in ids)
            await ProcessTaskAsync(id, ct);
    }

    public async Task ProcessTaskAsync(Guid id, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var task = await db.AccountCleanupTasks.SingleOrDefaultAsync(x => x.Id == id, ct);
                if (task == null) return;
                if (task.DeleteAccount)
                {
                    if (task.FirstSweepAt == null)
                    {
                        if (task.IdentitySubject is { } subject) await identity.DeleteAsync(subject, ct);
                        await RemoveAccountDataAsync(task, ct);
                    }
                    foreach (var key in JsonSerializer.Deserialize<string[]>(task.ObjectKeysJson ?? "[]") ?? [])
                        if (OwnsKey(task.UserId, key)) await storage.DeleteAsync(key, ct);
                    if (task.FirstSweepAt == null)
                    {
                        task.FirstSweepAt = Now;
                        task.Email = null;
                        task.IdentitySubject = null;
                        task.NextAttemptAt = Now.AddMinutes(10);
                        task.LastError = null;
                    }
                    else db.AccountCleanupTasks.Remove(task);
                }
                else
                {
                    if (task.ObjectKey is { } key && OwnsKey(task.UserId, key)) await storage.DeleteAsync(key, ct);
                    db.AccountCleanupTasks.Remove(task);
                }
                await db.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                db.ChangeTracker.Clear();
                var task = await db.AccountCleanupTasks.SingleOrDefaultAsync(x => x.Id == id, ct);
                if (task == null) return;
                task.Attempts++;
                task.LastError = error.GetType().Name;
                task.NextAttemptAt = Now.AddSeconds(Math.Min(3600, 5 * Math.Pow(2, Math.Min(task.Attempts, 10))));
                await db.SaveChangesAsync(ct);
                logger?.LogError(error, "Account cleanup task {TaskId} will be retried.", id);
            }
        }
    private async Task RemoveAccountDataAsync(AccountCleanupTask task, CancellationToken ct)
    {
        await AuthUserTransaction.RunAsync(db, task.Email ?? "", async () =>
        {
            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == task.UserId, ct);
            if (user == null) return true;
            if (user.DeletionRequestedAt == null) throw new InvalidOperationException("Account deletion was not requested.");
            var userId = user.Id;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            void Add(string? key) { if (key != null && OwnsKey(userId, key)) keys.Add(key); }
            Add(user.ProfileImageObjectKey);
            var images = await db.Images.Where(x => x.UserId == userId).ToListAsync(ct);
            foreach (var image in images) { Add(image.OriginalObjectKey); Add(image.OriginalObjectKey + ".gallery-480.jpg"); }
            foreach (var key in await db.OptimizedImages.Where(x => x.Image.UserId == userId).Select(x => x.ObjectKey).ToListAsync(ct)) Add(key);
            foreach (var key in await db.ImageLayers.Where(x => x.Project.UserId == userId).Select(x => x.ObjectKey).ToListAsync(ct)) Add(key);
            foreach (var key in await db.ProcessingJobs.Where(x => x.Project.UserId == userId).Select(x => x.ResultObjectKey).ToListAsync(ct)) Add(key);
            foreach (var key in await db.ProcessingJobs.Where(x => x.Project.UserId == userId).Select(x => x.SubjectMaskObjectKey).ToListAsync(ct)) Add(key);
            foreach (var key in await db.AccountCleanupTasks.Where(x => x.UserId == userId && x.ObjectKey != null).Select(x => x.ObjectKey).ToListAsync(ct)) Add(key);
            var assets = await db.WallpaperAssets.Where(x => x.UserId == userId).ToListAsync(ct);
            foreach (var asset in assets)
            {
                using var json = JsonDocument.Parse(asset.ManifestJson);
                if (json.RootElement.TryGetProperty("Files", out var files) || json.RootElement.TryGetProperty("files", out files))
                    foreach (var file in files.EnumerateArray())
                        if (file.TryGetProperty("Source", out var source) || file.TryGetProperty("source", out source)) Add(source.GetString());
            }
            if (db.Database.IsNpgsql() && await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.inpainting_jobs') IS NOT NULL AS \"Value\"").SingleAsync(ct))
                foreach (var row in await db.Database.SqlQuery<InpaintingKeys>($"SELECT \"InputObjectKey\", \"MaskObjectKey\", \"ResultObjectKey\" FROM inpainting_jobs WHERE \"UserId\" = {userId}").ToListAsync(ct))
                { Add(row.InputObjectKey); Add(row.MaskObjectKey); Add(row.ResultObjectKey); }
            task.ObjectKeysJson = JsonSerializer.Serialize(keys);
            var assetIds = assets.Select(x => x.Id).ToArray();
            db.DeviceCommands.RemoveRange(await db.DeviceCommands.Where(x => x.Device.UserId == userId).ToListAsync(ct));
            db.WallpaperAssignments.RemoveRange(await db.WallpaperAssignments.Where(x => x.DeviceDisplay.Device.UserId == userId).ToListAsync(ct));
            db.DeviceCommands.RemoveRange(await db.DeviceCommands.Where(x => x.Device.UserId != userId && assetIds.Contains((Guid)x.WallpaperAssetId!)).ToListAsync(ct));
            foreach (var assignment in await db.WallpaperAssignments.Where(x => x.DeviceDisplay.Device.UserId != userId &&
                (x.DesiredAssetId != null && assetIds.Contains(x.DesiredAssetId.Value) || x.ReportedAssetId != null && assetIds.Contains(x.ReportedAssetId.Value))).ToListAsync(ct))
            {
                if (assignment.DesiredAssetId is { } desired && assetIds.Contains(desired)) assignment.DesiredAssetId = null;
                if (assignment.ReportedAssetId is { } reported && assetIds.Contains(reported)) assignment.ReportedAssetId = null;
                assignment.Observation = WallpaperObservation.Unknown;
            }
            var postIds = await db.GalleryPosts.Where(x => x.UserId == userId || x.Image.UserId == userId).Select(x => x.Id).ToListAsync(ct);
            db.Comments.RemoveRange(await db.Comments.Where(x => x.UserId == userId || postIds.Contains(x.GalleryPostId)).ToListAsync(ct));
            db.GalleryLikes.RemoveRange(await db.GalleryLikes.Where(x => x.UserId == userId || postIds.Contains(x.GalleryPostId)).ToListAsync(ct));
            db.GalleryPosts.RemoveRange(await db.GalleryPosts.Where(x => postIds.Contains(x.Id)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            db.WallpaperAssets.RemoveRange(assets);
            db.ProcessingJobs.RemoveRange(await db.ProcessingJobs.Where(x => x.Project.UserId == userId).ToListAsync(ct));
            db.ImageLayers.RemoveRange(await db.ImageLayers.Where(x => x.Project.UserId == userId).ToListAsync(ct));
            db.LiveLayerProjects.RemoveRange(await db.LiveLayerProjects.Where(x => x.UserId == userId).ToListAsync(ct));
            db.OptimizedImages.RemoveRange(await db.OptimizedImages.Where(x => x.Image.UserId == userId || x.Device.UserId == userId).ToListAsync(ct));
            db.DeviceDisplays.RemoveRange(await db.DeviceDisplays.Where(x => x.Device.UserId == userId).ToListAsync(ct));
            db.Devices.RemoveRange(await db.Devices.Where(x => x.UserId == userId).ToListAsync(ct));
            db.UserSettings.RemoveRange(await db.UserSettings.Where(x => x.UserId == userId).ToListAsync(ct));
            db.AuthSessions.RemoveRange(await db.AuthSessions.Where(x => x.UserId == userId).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            db.Images.RemoveRange(images);
            db.Users.Remove(user);
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }
    private static bool OwnsKey(Guid userId, string key) => key.StartsWith($"users/{userId:N}/", StringComparison.Ordinal);
    private sealed class InpaintingKeys
    {
        public string? InputObjectKey { get; set; }
        public string? MaskObjectKey { get; set; }
        public string? ResultObjectKey { get; set; }
    }
}
