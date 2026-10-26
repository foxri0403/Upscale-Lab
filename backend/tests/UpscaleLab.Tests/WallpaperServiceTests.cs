using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Wallpapers;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Projects;
using UpscaleLab.Infrastructure.Wallpapers;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class WallpaperServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Snapshot_SurvivesProjectDeletionAndGeneratesFreshDownloadUrls()
    {
        await using var db = WorkflowData.Db();
        var store = new WorkflowStore();
        store.Files["source.png"] = [1, 2, 3];
        var project = WorkflowData.Project(Guid.NewGuid());
        db.Add(project); await db.SaveChangesAsync();
        var service = new WallpaperService(db, store, store);
        var asset = await service.CreateAsync(project.UserId, new(project.Id, "Saved", WallpaperMode.Static), Ct);
        var stored = JsonSerializer.Deserialize<WallpaperManifest>((await db.WallpaperAssets.SingleAsync()).ManifestJson)!;
        Assert.NotEqual("source.png", stored.Files.Single().Source);
        await new ProjectService(db, store).DeleteAsync(project.UserId, project.Id, Ct);
        Assert.False(store.Files.ContainsKey("source.png"));
        Assert.Equal(new byte[] { 1, 2, 3 }, store.Files[stored.Files.Single().Source]);
        var first = await service.GetManifestAsync(project.UserId, asset.Id, false, Ct);
        var second = await service.GetManifestAsync(project.UserId, asset.Id, false, Ct);
        Assert.NotEqual(first.Files.Single().Source, second.Files.Single().Source);
        Assert.NotNull(first.UrlsExpireAt);
        Assert.DoesNotContain("https://", (await db.WallpaperAssets.SingleAsync()).ManifestJson);
    }

    [Fact]
    public async Task SharingAndImport_ProduceAnIndependentOwnedCopy()
    {
        await using var db = WorkflowData.Db();
        var store = new WorkflowStore(); store.Files["source.png"] = [1];
        var project = WorkflowData.Project(Guid.NewGuid());
        db.Add(project); await db.SaveChangesAsync();
        var service = new WallpaperService(db, store, store);
        var asset = await service.CreateAsync(project.UserId, new(project.Id, "Saved", WallpaperMode.Static), Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetManifestAsync(null, asset.Id, false, Ct));
        var other = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() => service.ImportAsync(other, asset.Id, Ct));
        await service.SetSharingAsync(project.UserId, asset.Id, true, Ct);
        Assert.Single(await service.ListAsync(null, true, Ct));
        var imported = await service.ImportAsync(other, asset.Id, Ct);
        await service.ArchiveAsync(project.UserId, asset.Id, Ct);
        Assert.Empty(await service.ListAsync(null, true, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetManifestAsync(null, asset.Id, false, Ct));
        Assert.NotNull(await service.GetManifestAsync(other, imported.Id, false, Ct));
        Assert.NotNull(await service.GetManifestAsync(project.UserId, asset.Id, true, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.SetSharingAsync(other, asset.Id, true, Ct));
    }

    [Fact]
    public async Task FailedLayerCopy_RemovesPartialFilesAndDoesNotPublishAnAsset()
    {
        await using var db = WorkflowData.Db();
        var store = new WorkflowStore { FailCopyAfter = 1 };
        var project = WorkflowData.Project(Guid.NewGuid());
        project.Layers.Add(new ImageLayer { LayerType = LayerType.Background, LayerOrder = 0, ObjectKey = "background.png" });
        project.Layers.Add(new ImageLayer { LayerType = LayerType.Body, LayerOrder = 1, ObjectKey = "foreground.png" });
        store.Files["background.png"] = [1]; store.Files["foreground.png"] = [2];
        db.Add(project); await db.SaveChangesAsync();
        var service = new WallpaperService(db, store, store);
        await Assert.ThrowsAsync<IOException>(() => service.CreateAsync(project.UserId, new(project.Id, "Layers", WallpaperMode.Live2D), Ct));
        Assert.Empty(await db.WallpaperAssets.ToListAsync());
        Assert.Equal(2, store.Files.Count);
    }
}
