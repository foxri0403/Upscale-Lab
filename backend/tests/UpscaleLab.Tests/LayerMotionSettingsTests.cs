using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Wallpapers;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Projects;
using UpscaleLab.Infrastructure.Wallpapers;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class LayerMotionSettingsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static LiveLayerProject Layers(WorkflowStore store)
    {
        var p = WorkflowData.Project(Guid.NewGuid());
        p.Layers.Add(new() { LayerType = LayerType.Background, LayerOrder = 0, ObjectKey = "bg.png" });
        p.Layers.Add(new() { LayerType = LayerType.Body, LayerOrder = 1, ObjectKey = "subject.png" });
        store.Files["bg.png"] = [1]; store.Files["subject.png"] = [2]; return p;
    }

    [Fact]
    public async Task BothInputChoicesAndStrengthPersistWithoutEnablingLive2D()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); var p = Layers(store);
        db.Add(p); await db.SaveChangesAsync(); var service = new ProjectService(db, store);
        await service.SaveEditAsync(p.UserId, p.Id, new(83, "original", "original", 1, true, MouseEnabled: true, GyroEnabled: true), Ct);
        db.ChangeTracker.Clear(); var detail = await service.GetAsync(p.UserId, p.Id, Ct);
        Assert.True(detail.Edit!.MouseEnabled); Assert.True(detail.Edit.GyroEnabled); Assert.False(detail.Edit.LiveEnabled);
        Assert.Equal(83, detail.Edit.Strength); Assert.Equal(new[] { "배경 분리" }, detail.Features);
        // Clients which do not yet send input selections must preserve the stored values.
        var legacy = await service.SaveEditAsync(p.UserId, p.Id, new(40, "warm", "original", 1, true), Ct);
        Assert.True(legacy.MouseEnabled); Assert.True(legacy.GyroEnabled); Assert.False(legacy.LiveEnabled);
        await service.ResetEffectsAsync(p.UserId, p.Id, new("Layers"), Ct);
        var reset = await service.GetAsync(p.UserId, p.Id, Ct);
        Assert.False(reset.Edit!.MouseEnabled); Assert.False(reset.Edit.GyroEnabled);
    }

    [Fact]
    public async Task LayerMotionRequiresSeparationAndCannotChangeAnotherUsersImage()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); var p = WorkflowData.Project(Guid.NewGuid());
        db.Add(p); await db.SaveChangesAsync(); var service = new ProjectService(db, store);
        var edit = new SaveImageEditRequest(50, "original", "original", 1, true, MouseEnabled: true);
        await Assert.ThrowsAsync<ConflictException>(() => service.SaveEditAsync(p.UserId, p.Id, edit, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.SaveEditAsync(Guid.NewGuid(), p.Id, edit, Ct));
        await Assert.ThrowsAsync<ValidationException>(() => service.SaveEditAsync(p.UserId, p.Id, edit with { Strength = 101 }, Ct));
    }

    [Fact]
    public async Task ReservedLive2DStateDoesNotRequireOrActivateBackgroundSeparation()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); var p = WorkflowData.Project(Guid.NewGuid());
        db.Add(p); await db.SaveChangesAsync(); var service = new ProjectService(db, store);
        await service.SaveEditAsync(p.UserId, p.Id, new(50, "original", "original", 1, true, LiveEnabled: true), Ct);
        var loaded = await service.GetAsync(p.UserId, p.Id, Ct);
        Assert.Equal(new[] { "Live 2D" }, loaded.Features); Assert.Empty(loaded.Layers); Assert.False(loaded.Edit!.MouseEnabled);
    }

    [Fact]
    public async Task LayerSnapshotsKeepInputSelectionsSeparateFromLive2DAndVersionChangedGyroChoice()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); var p = Layers(store);
        p.SavedEditJson = ImageEditState.Create(new(65, "original", "original", 1, true, MouseEnabled: true)).ToJson();
        db.Add(p); await db.SaveChangesAsync(); var service = new WallpaperService(db, store, store);
        var asset = await service.CreateAsync(p.UserId, new(p.Id, "Separated", WallpaperMode.Live2D,
            MotionEnabled: true, MouseEnabled: true, GyroEnabled: false), Ct);
        Assert.True(asset.Edit!.MouseEnabled); Assert.False(asset.Edit.LiveEnabled); Assert.DoesNotContain("Live 2D", asset.Features!);
        var withGyro = await service.SnapshotAsync(p.UserId, asset.Id, new(true, 65, true, true), Ct);
        Assert.NotEqual(asset.Id, withGyro.Id); Assert.True(withGyro.Edit!.GyroEnabled); Assert.True(withGyro.Edit.MouseEnabled);
        Assert.False(withGyro.Edit.LiveEnabled); Assert.DoesNotContain("Live 2D", withGyro.Features!);
        Assert.False((await service.ListAsync(p.UserId, false, Ct)).Single(x => x.Id == asset.Id).Edit!.GyroEnabled);
        var off = await service.SnapshotAsync(p.UserId, withGyro.Id, new(false, 65, false, true), Ct);
        Assert.False(off.Edit!.MouseEnabled); Assert.True(off.Edit.GyroEnabled); Assert.False(off.Edit.LiveEnabled);
        Assert.False((await service.GetManifestAsync(p.UserId, off.Id, false, Ct)).MotionEnabled);
    }
}
