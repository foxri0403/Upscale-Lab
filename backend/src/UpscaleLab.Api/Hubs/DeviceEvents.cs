using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Devices;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Api.Hubs;

public sealed class DeviceEvents(IHubContext<NotificationHub> hub, ApplicationDbContext db,
    IActiveSessionValidator sessions, TimeProvider clock, ILogger<DeviceEvents> logger, DeviceChangeFeed feed) : IDeviceEvents
{
    public async Task ChangedAsync(Guid userId, Guid deviceId, CancellationToken ct, bool notifyAgent = false)
    {
        feed.Publish(userId, deviceId, notifyAgent);
        try
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var ids = await db.AuthSessions.AsNoTracking().Where(x => x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > now)
                .Select(x => x.Id).ToListAsync(ct);
            var groups = new List<string>();
            foreach (var id in ids)
                if (await sessions.IsActiveAsync(userId, id, ct)) groups.Add($"session:{id}");
            if (groups.Count > 0) await hub.Clients.Groups(groups).SendAsync("DeviceChanged", new { deviceId }, ct);
        }
        catch (Exception exception) { logger.LogWarning(exception, "Device notification failed for {DeviceId}", deviceId); }
    }
}
