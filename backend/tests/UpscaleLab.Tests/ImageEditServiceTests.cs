using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Projects;
using UpscaleLab.Infrastructure.Wallpapers;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ImageEditServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly SaveImageEditRequest Initial = new(35, "warm", "wide", 2, true);

    [Fact]
    public async Task RepeatedProjectSavesUpdateOneItemAndSurviveReloadWithoutCopyingFiles()
    {
        await using var db = WorkflowData.Db();
        var store = new WorkflowStore(); store.Files["source.png"] = [1, 2, 3];
        var project = WorkflowData.Project(Guid.NewGuid()); db.Add(project); await db.SaveChangesAsync();
        var service = new ProjectService(db, store);
        var first = await service.SaveEditAsync(project.UserId, project.Id, Initial, Ct);
        var second = await service.SaveEditAsync(project.UserId, project.Id, Initial with { Filter = "mono", Crop = "square" }, Ct);
        db.ChangeTracker.Clear();
        var reloaded = new ProjectService(db, store);
        var detail = await reloaded.GetAsync(project.UserId, project.Id, Ct);
        Assert.Equal(project.Id, detail.Id);
        Assert.Equal(second, detail.Edit);
        Assert.Equal(second, Assert.Single(await reloaded.GetAllAsync(project.UserId, Ct)).Edit);
        Assert.Equal(DateTimeKind.Utc, first.SavedAt.Kind);
        Assert.True(second.SavedAt >= first.SavedAt);
        Assert.Empty(await db.WallpaperAssets.ToListAsync());
        Assert.Single(store.Files);
        Assert.Equal(new byte[] { 1, 2, 3 }, store.Files["source.png"]);
    }

    [Fact]
    public async Task AssetEditsUpdateTheSameLibraryItemAndKeepDeviceManifestImmutable()
    {
        await using var db = WorkflowData.Db();
        var store = new WorkflowStore(); store.Files["source.png"] = [1, 2, 3];
        var project = WorkflowData.Project(Guid.NewGuid()); db.Add(project); await db.SaveChangesAsync();
        var service = new WallpaperService(db, store, store);
        var summary = await service.CreateAsync(project.UserId, new(project.Id, "Saved", Domain.Enums.WallpaperMode.Static), Ct);
        var manifest = (await db.WallpaperAssets.SingleAsync()).ManifestJson;
        var count = store.Files.Count;
        await service.SaveEditAsync(project.UserId, summary.Id, Initial, Ct);
        var saved = await service.SaveEditAsync(project.UserId, summary.Id, Initial with { Strength = 50 }, Ct);
        db.ChangeTracker.Clear();
        var item = Assert.Single(await new WallpaperService(db, store, store).ListAsync(project.UserId, false, Ct));
        Assert.Equal(summary.Id, item.Id);
        Assert.Equal(saved, item.Edit);
        Assert.Equal(manifest, (await db.WallpaperAssets.SingleAsync()).ManifestJson);
        Assert.Equal(count, store.Files.Count);
    }

    [Fact]
    public async Task SavesEnforceOwnershipAndSharedProjectAndAssetLocks()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore();
        var project = WorkflowData.Project(Guid.NewGuid()); db.Add(project);
        var asset = new WallpaperAsset { UserId = project.UserId, SourceProjectId = project.Id, IsPublic = true };
        db.Add(asset); await db.SaveChangesAsync();
        var projects = new ProjectService(db, store); var assets = new WallpaperService(db, store, store);
        await Assert.ThrowsAsync<NotFoundException>(() => projects.SaveEditAsync(Guid.NewGuid(), project.Id, Initial, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => assets.SaveEditAsync(Guid.NewGuid(), asset.Id, Initial, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => projects.SaveEditAsync(project.UserId, project.Id, Initial, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => assets.SaveEditAsync(project.UserId, asset.Id, Initial, Ct));
        asset.IsPublic = false; await db.SaveChangesAsync();
        await projects.SaveEditAsync(project.UserId, project.Id, Initial, Ct);
        await assets.SaveEditAsync(project.UserId, asset.Id, Initial, Ct);
        asset.IsArchived = true; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => assets.SaveEditAsync(project.UserId, asset.Id, Initial, Ct));
    }

    [Theory]
    [InlineData(-1, "original", "original", 1)]
    [InlineData(101, "original", "original", 1)]
    [InlineData(35, "unknown", "original", 1)]
    [InlineData(35, "original", "unknown", 1)]
    [InlineData(35, "original", "original", 3)]
    public async Task InvalidSettingsDoNotOverwriteSavedEdits(int strength, string filter, string crop, int scale)
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore();
        var project = WorkflowData.Project(Guid.NewGuid()); db.Add(project); await db.SaveChangesAsync();
        var service = new ProjectService(db, store);
        var saved = await service.SaveEditAsync(project.UserId, project.Id, Initial, Ct);
        await Assert.ThrowsAsync<ValidationException>(() => service.SaveEditAsync(project.UserId, project.Id,
            new(strength, filter, crop, scale, true), Ct));
        Assert.Equal(saved, (await service.GetAsync(project.UserId, project.Id, Ct)).Edit);
    }
}
