using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Devices;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Devices;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class DeviceControlServiceTests
{
    [Fact]
    public async Task QueuedCommands_SurviveContextRestartAndCannotBeAcknowledgedByAnotherDevice()
    {
        var options = new DbContextOptionsBuilder<UpscaleLab.Infrastructure.Database.ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var userId = Guid.NewGuid();
        Guid deviceId, commandId, otherId;
        await using (var db = new UpscaleLab.Infrastructure.Database.ApplicationDbContext(options))
        {
            var device = WorkflowData.Device(userId);
            var other = WorkflowData.Device(userId);
            var asset = new WallpaperAsset { UserId = userId, ManifestJson = "{}" };
            db.AddRange(device, other, asset); await db.SaveChangesAsync();
            var service = new DeviceControlService(db, new WorkflowClock());
            await service.RotateTokenAsync(userId, device.Id, Ct);
            await service.RotateTokenAsync(userId, other.Id, Ct);
            var state = await service.HeartbeatAsync(new(device.Id, userId), Inventory(1, Screen()), Ct);
            var command = await service.ApplyAsync(userId, device.Id,
                new(Guid.NewGuid(), state.Displays.Single().Id, asset.Id, WallpaperTarget.Desktop), Ct);
            deviceId = device.Id; commandId = command.Id; otherId = other.Id;
        }
        await using var restarted = new UpscaleLab.Infrastructure.Database.ApplicationDbContext(options);
        var restored = new DeviceControlService(restarted, new WorkflowClock());
        Assert.Equal(commandId, (await restored.GetCommandsAsync(userId, deviceId, true, Ct)).Single().Id);
        await Assert.ThrowsAsync<NotFoundException>(() => restored.ReportAsync(new(otherId, userId), commandId, new(DeviceCommandStatus.Applied), Ct));
    }

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static AgentHeartbeat Inventory(long revision, params DisplayInventory[] displays) =>
        new(revision, "Windows", "1.0.0", true, false, displays);
    private static DisplayInventory Screen(string nativeId = "monitor-1", IReadOnlyList<WallpaperReport>? reports = null) =>
        new(nativeId, "Monitor", 1920, 1080, Wallpapers: reports);

    [Fact]
    public async Task Credentials_AreHashedRotatedRevokedAndOwned()
    {
        await using var db = WorkflowData.Db();
        var device = WorkflowData.Device(Guid.NewGuid());
        db.Devices.Add(device); await db.SaveChangesAsync();
        var service = new DeviceControlService(db, new WorkflowClock());
        var first = await service.RotateTokenAsync(device.UserId, device.Id, Ct);
        Assert.NotEqual(first.Token, device.AgentTokenHash);
        Assert.Equal(device.Id, (await service.AuthenticateAsync(first.Token, Ct))!.DeviceId);
        var second = await service.RotateTokenAsync(device.UserId, device.Id, Ct);
        Assert.Null(await service.AuthenticateAsync(first.Token, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.RotateTokenAsync(Guid.NewGuid(), device.Id, Ct));
        await service.RevokeTokenAsync(device.UserId, device.Id, Ct);
        Assert.Null(await service.AuthenticateAsync(second.Token, Ct));
    }

    [Fact]
    public async Task Inventory_PreservesDisconnectedDisplaysAndRejectsStaleReports()
    {
        await using var db = WorkflowData.Db();
        var device = WorkflowData.Device(Guid.NewGuid());
        db.Add(device); await db.SaveChangesAsync();
        var service = new DeviceControlService(db, new WorkflowClock());
        await service.RotateTokenAsync(device.UserId, device.Id, Ct);
        var agent = new AgentIdentity(device.Id, device.UserId);
        var first = await service.HeartbeatAsync(agent, Inventory(1, Screen(), Screen("monitor-2")), Ct);
        var second = await service.HeartbeatAsync(agent, Inventory(2, Screen()), Ct);
        Assert.Equal(2, second.Displays.Count);
        Assert.False(second.Displays.Single(x => x.NativeId == "monitor-2").IsConnected);
        Assert.Equal(first.Displays.Single(x => x.NativeId == "monitor-1").Id,
            second.Displays.Single(x => x.NativeId == "monitor-1").Id);
        await Assert.ThrowsAsync<ConflictException>(() => service.HeartbeatAsync(agent, Inventory(1, Screen()), Ct));
    }

    [Fact]
    public async Task Apply_IsIdempotentSupersedesOldRequestsAndRequiresActualAcknowledgement()
    {
        await using var db = WorkflowData.Db();
        var clock = new WorkflowClock();
        var device = WorkflowData.Device(Guid.NewGuid());
        var asset = new WallpaperAsset { UserId = device.UserId, Title = "Wallpaper", ManifestJson = "{}" };
        db.AddRange(device, asset); await db.SaveChangesAsync();
        var service = new DeviceControlService(db, clock);
        await service.RotateTokenAsync(device.UserId, device.Id, Ct);
        var agent = new AgentIdentity(device.Id, device.UserId);
        var inventory = await service.HeartbeatAsync(agent, Inventory(1, Screen()), Ct);
        var displayId = inventory.Displays.Single().Id;
        var request = new ApplyWallpaperRequest(Guid.NewGuid(), displayId, asset.Id, WallpaperTarget.Desktop);
        var first = await service.ApplyAsync(device.UserId, device.Id, request, Ct);
        var duplicate = await service.ApplyAsync(device.UserId, device.Id, request, Ct);
        Assert.Equal(first.Id, duplicate.Id);
        Assert.Null((await service.GetStateAsync(device.UserId, device.Id, Ct)).Displays.Single().Wallpapers.Single().ReportedAssetId);
        var latest = await service.ApplyAsync(device.UserId, device.Id, request with { RequestId = Guid.NewGuid() }, Ct);
        await Assert.ThrowsAsync<ConflictException>(() => service.ReportAsync(agent, first.Id, new(DeviceCommandStatus.Applied), Ct));
        Assert.Equal(latest.Id, (await service.GetCommandsAsync(device.UserId, device.Id, true, Ct)).Single().Id);
        await service.ReportAsync(agent, latest.Id, new(DeviceCommandStatus.NeedsUserAction), Ct);
        Assert.Null((await service.GetStateAsync(device.UserId, device.Id, Ct)).Displays.Single().Wallpapers.Single().ReportedAssetId);
        await service.ReportAsync(agent, latest.Id, new(DeviceCommandStatus.Applied), Ct);
        var applied = (await service.GetStateAsync(device.UserId, device.Id, Ct)).Displays.Single().Wallpapers.Single();
        Assert.Equal(asset.Id, applied.ReportedAssetId);
        Assert.Equal(WallpaperObservation.Managed, applied.Observation);
        Assert.Equal(DeviceCommandStatus.Applied, (await service.ReportAsync(agent, latest.Id, new(DeviceCommandStatus.Applied), Ct)).Status);
        Assert.Empty(await service.GetCommandsAsync(device.UserId, device.Id, true, Ct));
    }

    [Fact]
    public async Task Commands_ExpireAndRejectCrossUserOrMismatchedIdempotencyKeys()
    {
        await using var db = WorkflowData.Db();
        var clock = new WorkflowClock();
        var device = WorkflowData.Device(Guid.NewGuid());
        var asset = new WallpaperAsset { UserId = device.UserId, ManifestJson = "{}" };
        var foreign = new WallpaperAsset { UserId = Guid.NewGuid(), ManifestJson = "{}" };
        db.AddRange(device, asset, foreign); await db.SaveChangesAsync();
        var service = new DeviceControlService(db, clock);
        await service.RotateTokenAsync(device.UserId, device.Id, Ct);
        var agent = new AgentIdentity(device.Id, device.UserId);
        var state = await service.HeartbeatAsync(agent, Inventory(1, Screen()), Ct);
        var request = new ApplyWallpaperRequest(Guid.NewGuid(), state.Displays.Single().Id, asset.Id, WallpaperTarget.Desktop);
        await Assert.ThrowsAsync<NotFoundException>(() => service.ApplyAsync(device.UserId, device.Id, request with { AssetId = foreign.Id }, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetStateAsync(foreign.UserId, device.Id, Ct));
        var command = await service.ApplyAsync(device.UserId, device.Id, request, Ct);
        await Assert.ThrowsAsync<ConflictException>(() => service.ApplyAsync(device.UserId, device.Id, request with { AssetId = foreign.Id }, Ct));
        clock.Time = clock.Time.AddDays(2);
        Assert.Empty(await service.GetCommandsAsync(device.UserId, device.Id, true, Ct));
        Assert.Equal(DeviceCommandStatus.Expired, (await db.DeviceCommands.SingleAsync()).Status);
        await Assert.ThrowsAsync<ConflictException>(() => service.GetCommandAssetAsync(agent, command.Id, Ct));
    }

    [Fact]
    public async Task ExternalWallpaperReport_DoesNotClaimTheDesiredImageIsApplied()
    {
        await using var db = WorkflowData.Db();
        var device = WorkflowData.Device(Guid.NewGuid());
        db.Add(device); await db.SaveChangesAsync();
        var service = new DeviceControlService(db, new WorkflowClock());
        await service.RotateTokenAsync(device.UserId, device.Id, Ct);
        var state = await service.HeartbeatAsync(new(device.Id, device.UserId), Inventory(1,
            Screen(reports: [new(WallpaperTarget.Desktop, WallpaperObservation.Unavailable)])), Ct);
        var observed = state.Displays.Single().Wallpapers.Single();
        Assert.Equal(WallpaperObservation.Unavailable, observed.Observation);
        Assert.Null(observed.ReportedAssetId);
        await Assert.ThrowsAsync<ValidationException>(() => service.HeartbeatAsync(new(device.Id, device.UserId),
            Inventory(2, Screen(reports: [new(WallpaperTarget.Desktop, WallpaperObservation.Managed)])), Ct));
        Assert.Equal(1, device.InventoryRevision);
    }
}
