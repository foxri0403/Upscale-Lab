using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Devices;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Devices;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class DeviceWallpaperRemovalTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static async Task<(Device, WallpaperAsset, DeviceControlService)> Seed(ApplicationDbContext db, WorkflowClock? clock = null)
    {
        var d = WorkflowData.Device(Guid.NewGuid()); d.SupportsWallpaperRemoval = true;
        var asset = new WallpaperAsset { UserId = d.UserId }; db.AddRange(d, asset); await db.SaveChangesAsync();
        var service = new DeviceControlService(db, clock ?? new WorkflowClock()); await service.RotateTokenAsync(d.UserId, d.Id, Ct);
        await service.HeartbeatAsync(new(d.Id, d.UserId), new(1, "Windows", "1.2.0", true, false,
            [new("one", "Monitor 1", 1920, 1080, Wallpapers: [new(WallpaperTarget.Desktop, WallpaperObservation.Managed, asset.Id)]),
             new("two", "Monitor 2", 1920, 1080, Wallpapers: [new(WallpaperTarget.Desktop, WallpaperObservation.Managed, asset.Id)])], SupportsWallpaperRemoval: true), Ct);
        return (d, asset, service);
    }

    [Fact]
    public async Task RemovalIsQueuedWithoutClaimingCompletionAndAffectsOnlyTheConfirmedScreen()
    {
        await using var db = WorkflowData.Db(); var (d, asset, service) = await Seed(db);
        var display = d.Displays.Single(x => x.NativeId == "one");
        var command = await service.ClearAsync(d.UserId, d.Id, new(Guid.NewGuid(), display.Id, WallpaperTarget.Desktop), Ct);
        Assert.Equal(DeviceCommandAction.Clear, command.Action); Assert.Null(command.AssetId); Assert.Equal(DeviceCommandStatus.Pending, command.Status);
        var pending = (await service.GetStateAsync(d.UserId, d.Id, Ct)).Displays.Single(x => x.Id == display.Id).Wallpapers.Single();
        Assert.True(pending.PendingRemoval); Assert.Equal(asset.Id, pending.ReportedAssetId); Assert.Equal(WallpaperObservation.Managed, pending.Observation);
        await Assert.ThrowsAsync<ConflictException>(() => service.GetCommandAssetAsync(new(d.Id, d.UserId), command.Id, Ct));
        Assert.Equal(command.Id, (await service.GetCommandAsync(new(d.Id, d.UserId), command.Id, Ct)).Id);
        await service.ReportAsync(new(d.Id, d.UserId), command.Id, new(DeviceCommandStatus.Applied), Ct);
        var state = await service.GetStateAsync(d.UserId, d.Id, Ct);
        var removed = state.Displays.Single(x => x.Id == display.Id).Wallpapers.Single();
        Assert.Null(removed.ReportedAssetId); Assert.False(removed.PendingRemoval); Assert.Equal(WallpaperObservation.Cleared, removed.Observation);
        Assert.Equal(asset.Id, state.Displays.Single(x => x.Id != display.Id).Wallpapers.Single().ReportedAssetId);
        Assert.Single(await db.WallpaperAssets.ToListAsync());
    }

    [Fact]
    public async Task RetryingTheSameRemovalIsIdempotentAndReusingItsIdForAnotherActionIsRejected()
    {
        await using var db = WorkflowData.Db(); var (d, asset, service) = await Seed(db); var display = d.Displays.First();
        var apply = await service.ApplyAsync(d.UserId, d.Id, new(Guid.NewGuid(), display.Id, asset.Id, WallpaperTarget.Desktop), Ct);
        var request = new ClearWallpaperRequest(Guid.NewGuid(), display.Id, WallpaperTarget.Desktop);
        var first = await service.ClearAsync(d.UserId, d.Id, request, Ct);
        var retry = await service.ClearAsync(d.UserId, d.Id, request, Ct);
        Assert.Equal(first.Id, retry.Id); Assert.Equal(first.Sequence, retry.Sequence);
        Assert.Equal(DeviceCommandStatus.Superseded, (await db.DeviceCommands.FindAsync(apply.Id))!.Status);
        await Assert.ThrowsAsync<ConflictException>(() => service.ClearAsync(d.UserId, d.Id, request with { DisplayId = d.Displays.Last().Id }, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => service.ApplyAsync(d.UserId, d.Id, new(request.RequestId, display.Id, asset.Id, WallpaperTarget.Desktop), Ct));
        await service.ApplyAsync(d.UserId, d.Id, new(Guid.NewGuid(), display.Id, asset.Id, WallpaperTarget.Desktop), Ct);
        await Assert.ThrowsAsync<ConflictException>(() => service.GetCommandAsync(new(d.Id, d.UserId), first.Id, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => service.ReportAsync(new(d.Id, d.UserId), first.Id, new(DeviceCommandStatus.Applied), Ct));
    }

    [Fact]
    public async Task RemovalRequiresOwnershipCapabilityAndAControllableConnectedDisplay()
    {
        await using var db = WorkflowData.Db(); var (d, _, service) = await Seed(db); var display = d.Displays.First();
        var request = new ClearWallpaperRequest(Guid.NewGuid(), display.Id, WallpaperTarget.Desktop);
        await Assert.ThrowsAsync<NotFoundException>(() => service.ClearAsync(Guid.NewGuid(), d.Id, request, Ct));
        d.SupportsWallpaperRemoval = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.ClearAsync(d.UserId, d.Id, request, Ct));
        d.SupportsWallpaperRemoval = true; display.CanApplyWallpaper = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.ClearAsync(d.UserId, d.Id, request, Ct));
        display.CanApplyWallpaper = true; display.IsConnected = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => service.ClearAsync(d.UserId, d.Id, request, Ct));
        Assert.Empty(await db.DeviceCommands.ToListAsync());
    }

    [Fact]
    public async Task FailedRemovalKeepsThePreviousObservedWallpaper()
    {
        await using var db = WorkflowData.Db(); var (d, asset, service) = await Seed(db); var display = d.Displays.First();
        var clear = await service.ClearAsync(d.UserId, d.Id, new(Guid.NewGuid(), display.Id, WallpaperTarget.Desktop), Ct);
        await service.ReportAsync(new(d.Id, d.UserId), clear.Id, new(DeviceCommandStatus.Failed, "OS rejected removal"), Ct);
        var observed = (await service.GetStateAsync(d.UserId, d.Id, Ct)).Displays.Single(x => x.Id == display.Id).Wallpapers.Single();
        Assert.Equal(asset.Id, observed.ReportedAssetId); Assert.Equal(WallpaperObservation.Managed, observed.Observation); Assert.False(observed.PendingRemoval);
    }

    [Fact]
    public async Task ExpiredOrRevokedCommandsCannotBeExecuted()
    {
        await using var db = WorkflowData.Db(); var clock = new WorkflowClock(); var (d, _, service) = await Seed(db, clock);
        var clear = await service.ClearAsync(d.UserId, d.Id, new(Guid.NewGuid(), d.Displays.First().Id, WallpaperTarget.Desktop), Ct);
        clock.Time = clock.Time.AddDays(2);
        await Assert.ThrowsAsync<ConflictException>(() => service.GetCommandAsync(new(d.Id, d.UserId), clear.Id, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => service.ReportAsync(new(d.Id, d.UserId), clear.Id, new(DeviceCommandStatus.Applied), Ct));
        await service.RevokeTokenAsync(d.UserId, d.Id, Ct);
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.GetCommandAsync(new(d.Id, d.UserId), clear.Id, Ct));
    }

    [Fact]
    public async Task OldAgentsNeverReceiveRemovalCommandsAfterCapabilityDowngrade()
    {
        await using var db = WorkflowData.Db(); var (d, _, service) = await Seed(db);
        await service.ClearAsync(d.UserId, d.Id, new(Guid.NewGuid(), d.Displays.First().Id, WallpaperTarget.Desktop), Ct);
        d.SupportsWallpaperRemoval = false; await db.SaveChangesAsync();
        Assert.Empty(await service.GetPendingCommandsAsync(d.UserId, d.Id, Ct));
        Assert.Equal(DeviceCommandStatus.Pending, (await db.DeviceCommands.SingleAsync()).Status);
    }

    [Fact]
    public async Task AndroidHomeAndLockRemovalAreIndependentAndNeedSupportedTargets()
    {
        await using var db = WorkflowData.Db(); var d = WorkflowData.Device(Guid.NewGuid()); d.Platform = DevicePlatform.Android;
        db.Add(d); await db.SaveChangesAsync(); var service = new DeviceControlService(db, new WorkflowClock());
        await service.RotateTokenAsync(d.UserId, d.Id, Ct);
        var state = await service.HeartbeatAsync(new(d.Id, d.UserId), new(1, "Android", "1.2.0", true, true,
            [new("android-built-in", "Phone", 1080, 2400, Wallpapers: [new(WallpaperTarget.Home, WallpaperObservation.Cleared), new(WallpaperTarget.Lock, WallpaperObservation.External)])], SupportsWallpaperRemoval: true), Ct);
        var command = await service.ClearAsync(d.UserId, d.Id, new(Guid.NewGuid(), state.Displays.Single().Id, WallpaperTarget.Lock), Ct);
        await service.ReportAsync(new(d.Id, d.UserId), command.Id, new(DeviceCommandStatus.Applied), Ct);
        var assignments = (await service.GetStateAsync(d.UserId, d.Id, Ct)).Displays.Single().Wallpapers;
        Assert.All(assignments, x => Assert.Equal(WallpaperObservation.Cleared, x.Observation));
        d.SupportsLockScreen = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => service.ClearAsync(d.UserId, d.Id, new(Guid.NewGuid(), state.Displays.Single().Id, WallpaperTarget.Lock), Ct));
    }
}
