using System.Text.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Gallery;
using UpscaleLab.Application.Images;
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
    public async Task MotionOffSnapshotKeepsBothLayersAndCannotChangeAnotherUsersWallpaper()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore();
        var project = WorkflowData.Project(Guid.NewGuid());
        project.Layers.Add(new ImageLayer { LayerType = LayerType.Background, LayerOrder = 0, ObjectKey = "background.png" });
        project.Layers.Add(new ImageLayer { LayerType = LayerType.Body, LayerOrder = 1, ObjectKey = "foreground.png", MovementX = 8, MovementY = 5 });
        store.Files["background.png"] = [1]; store.Files["foreground.png"] = [2];
        db.Add(project); await db.SaveChangesAsync(); var service = new WallpaperService(db, store, store);
        var original = await service.CreateAsync(project.UserId, new(project.Id, "Layers", WallpaperMode.Live2D), Ct);
        var disabled = await service.SnapshotAsync(project.UserId, original.Id, new(false, 35), Ct);
        Assert.NotEqual(original.Id, disabled.Id);
        var neutral = await service.GetManifestAsync(project.UserId, disabled.Id, false, Ct);
        Assert.Equal(WallpaperMode.Live2D, neutral.Mode); Assert.False(neutral.MotionEnabled); Assert.Equal(2, neutral.Files.Count);
        Assert.True((await service.GetManifestAsync(project.UserId, original.Id, false, Ct)).MotionEnabled);
        Assert.False(disabled.Edit!.LiveEnabled);
        Assert.DoesNotContain("Live 2D", disabled.Features!);
        var enabled = await service.SnapshotAsync(project.UserId, disabled.Id, new(true, 80), Ct);
        Assert.True((await service.GetManifestAsync(project.UserId, enabled.Id, false, Ct)).MotionEnabled);
        await Assert.ThrowsAsync<NotFoundException>(() => service.SnapshotAsync(Guid.NewGuid(), original.Id, new(false, 35), Ct));
    }

    [Fact]
    public async Task UnchangedApplicationReusesSnapshotAcrossServiceInstancesButChangedPixelsCreateAVersion()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore();
        var project = WorkflowData.Project(Guid.NewGuid()); store.Files["source.png"] = [1, 2, 3];
        db.Add(project); await db.SaveChangesAsync();
        var request = new CreateWallpaperRequest(project.Id, "Wallpaper", WallpaperMode.Static, ReuseExisting: true);
        var first = await new WallpaperService(db, store, store).CreateAsync(project.UserId, request, Ct);
        var second = await new WallpaperService(db, store, store).CreateAsync(project.UserId, request, Ct);
        Assert.Equal(first.Id, second.Id); Assert.Single(await db.WallpaperAssets.ToListAsync());
        Assert.Equal(2, store.Files.Count);
        project.OriginalImage.OriginalObjectKey = "changed.png"; store.Files["changed.png"] = [4, 5];
        await db.SaveChangesAsync();
        var changed = await new WallpaperService(db, store, store).CreateAsync(project.UserId, request, Ct);
        Assert.NotEqual(first.Id, changed.Id); Assert.Equal(2, await db.WallpaperAssets.CountAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => new WallpaperService(db, store, store).CreateAsync(Guid.NewGuid(), request, Ct));
    }
    [Fact]
    public async Task ArchivedSnapshotsAreNeverReusedAndExplicitVersionCreationStillCreatesAVersion()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore();
        var project = WorkflowData.Project(Guid.NewGuid()); store.Files["source.png"] = [1]; db.Add(project); await db.SaveChangesAsync();
        var service = new WallpaperService(db, store, store);
        var request = new CreateWallpaperRequest(project.Id, "Saved", WallpaperMode.Static, ReuseExisting: true);
        var first = await service.CreateAsync(project.UserId, request, Ct); await service.ArchiveAsync(project.UserId, first.Id, Ct);
        var second = await service.CreateAsync(project.UserId, request, Ct);
        var explicitVersion = await service.CreateAsync(project.UserId, request with { ReuseExisting = false }, Ct);
        Assert.NotEqual(first.Id, second.Id); Assert.NotEqual(second.Id, explicitVersion.Id);
    }

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
        Assert.Equal(0, Assert.Single(await service.ListAsync(null, true, Ct)).DownloadCount);
        await service.GetManifestAsync(other, asset.Id, false, Ct);
        Assert.Equal(0, Assert.Single(await service.ListAsync(null, true, Ct)).DownloadCount);
        Assert.Equal(new GalleryEngagement(1, true), await service.SetLikeAsync(other, asset.Id, true, Ct));
        Assert.Equal(new GalleryEngagement(1, true), await service.SetLikeAsync(other, asset.Id, true, Ct));
        store.FailCopyAfter = 0;
        await Assert.ThrowsAsync<IOException>(() => service.ImportAsync(other, asset.Id, Ct));
        Assert.Equal(0, Assert.Single(await service.ListAsync(other, true, Ct)).DownloadCount);
        store.FailCopyAfter = null;
        var imported = await service.ImportAsync(other, asset.Id, Ct);
        var shared = Assert.Single(await service.ListAsync(other, true, Ct, new ExploreQuery(Sort: "downloads")));
        Assert.Equal(1, shared.DownloadCount);
        Assert.Equal(1, shared.LikeCount);
        Assert.True(shared.IsLikedByCurrentUser);
        Assert.Equal(new GalleryEngagement(0, false), await service.SetLikeAsync(other, asset.Id, false, Ct));
        await service.ArchiveAsync(project.UserId, asset.Id, Ct);
        Assert.Empty(await service.ListAsync(null, true, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetManifestAsync(null, asset.Id, false, Ct));
        Assert.NotNull(await service.GetManifestAsync(other, imported.Id, false, Ct));
        Assert.NotNull(await service.GetManifestAsync(project.UserId, asset.Id, true, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.SetSharingAsync(other, asset.Id, true, Ct));
    }

    [Fact]
    public async Task ScreenFramingSavesTargetRatioAndPositionWithoutChangingThePublishedAsset()
    {
        await using var db = WorkflowData.Db();
        var store = new WorkflowStore();
        var project = WorkflowData.Project(Guid.NewGuid());
        project.OriginalImage.OriginalWidth = 90;
        project.OriginalImage.OriginalHeight = 160;
        using var source = new MagickImage(MagickColors.Red, 90, 160);
        store.Files["source.png"] = source.ToByteArray(MagickFormat.Png);
        db.Add(project);
        await db.SaveChangesAsync();
        var editor = new ProjectService(db, store);
        await editor.SaveEditAsync(project.UserId, project.Id, new(50, "original", "original", 1, true,
            Tags: ["풍경"], PlatformTags: ["mobile", "tablet"]), Ct);
        var service = new WallpaperService(db, store, store);
        var original = await service.CreateAsync(project.UserId, new(project.Id, "Portrait", WallpaperMode.Static), Ct);
        await service.SetSharingAsync(project.UserId, original.Id, true, Ct);
        var tagged = Assert.Single(await service.ListAsync(null, true, Ct,
            new ExploreQuery("Portrait", Tags: ["풍경"], PlatformTags: ["tablet"])));
        Assert.Equal(new[] { "mobile", "tablet" }, tagged.Edit!.PlatformTags);
        Assert.Empty(await service.ListAsync(null, true, Ct, new ExploreQuery(PlatformTags: ["pc"])));

        var frame = new WallpaperFrame(160, 90, true, 1, 1, .5);
        var framed = await service.SnapshotAsync(project.UserId, original.Id, new(false, 50,
            Render: new(50, "original", "original", 1, true, Frame: frame)), Ct);
        Assert.True(framed.IsDeviceVariant);
        Assert.False(framed.IsPublic);
        var manifest = JsonSerializer.Deserialize<WallpaperManifest>(
            (await db.WallpaperAssets.SingleAsync(x => x.Id == framed.Id)).ManifestJson)!;
        Assert.Equal((160, 90), (manifest.Width, manifest.Height));
        var file = manifest.Files.Single();
        Assert.Equal("image/png", file.ContentType);
        using var output = new MagickImage(store.Files[file.Source]);
        using var pixels = output.GetPixels();
        Assert.Equal(24, pixels.GetPixel(0, 45).ToColor()!.R);
        Assert.Equal(255, pixels.GetPixel(159, 45).ToColor()!.R);
        var untouched = await service.GetManifestAsync(null, original.Id, false, Ct);
        Assert.Equal((90, 160), (untouched.Width, untouched.Height));
    }

    [Fact]
    public async Task AuthorCanUpdatePublicMetadataWhileViewerCanOnlyFrameTheSavedCopy()
    {
        await using var db = WorkflowData.Db();
        var store = new WorkflowStore();
        var project = WorkflowData.Project(Guid.NewGuid());
        using var source = new MagickImage(MagickColors.Red, 10, 10);
        store.Files["source.png"] = source.ToByteArray(MagickFormat.Png);
        db.Add(project);
        await db.SaveChangesAsync();
        var service = new WallpaperService(db, store, store);
        var original = await service.CreateAsync(project.UserId, new(project.Id, "Original", WallpaperMode.Static), Ct);
        await service.SetSharingAsync(project.UserId, original.Id, true, Ct);
        var originalManifest = (await db.WallpaperAssets.FindAsync(original.Id))!.ManifestJson;
        var viewer = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateMetadataAsync(viewer, original.Id,
            new("Unauthorized", ["게임"], ["pc"]), Ct));
        await service.UpdateMetadataAsync(project.UserId, original.Id, new("Updated", ["풍경"], ["pc", "tablet"]), Ct);
        var listed = Assert.Single(await service.ListAsync(null, true, Ct,
            new ExploreQuery("Updated", Tags: ["풍경"], PlatformTags: ["tablet"])));
        Assert.Equal(new[] { "pc", "tablet" }, listed.Edit!.PlatformTags);
        Assert.Equal(originalManifest, (await db.WallpaperAssets.FindAsync(original.Id))!.ManifestJson);
        Assert.Equal("Updated", project.Title);
        Assert.Equal(new[] { "풍경" }, ImageEditState.FromJson(project.SavedEditJson)!.Tags);

        var imported = await service.ImportAsync(viewer, original.Id, Ct);
        Assert.True(imported.Edit!.IsReadOnly);
        await Assert.ThrowsAsync<ConflictException>(() => service.SaveEditAsync(viewer, imported.Id, new(50, "mono", "original", 1, true), Ct));
        await Assert.ThrowsAsync<ConflictException>(() => service.SetSharingAsync(viewer, imported.Id, true, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateMetadataAsync(viewer, imported.Id, new("Edit"), Ct));
        await Assert.ThrowsAsync<ConflictException>(() => service.SnapshotAsync(viewer, imported.Id, new(false, 50), Ct));
        var applied = await service.SnapshotAsync(viewer, imported.Id, new(false, 50,
            Render: new(50, "mono", "original", 1, true, Frame: new(160, 90, true, 1, 1, .5))), Ct);
        Assert.True(applied.IsDeviceVariant);
        Assert.True(applied.Edit!.IsReadOnly);
        var manifest = JsonSerializer.Deserialize<WallpaperManifest>((await db.WallpaperAssets.FindAsync(applied.Id))!.ManifestJson)!;
        Assert.Equal((160, 90), (manifest.Width, manifest.Height));
        using var result = new MagickImage(store.Files[manifest.Files.Single().Source]);
        using var pixels = result.GetPixels();
        Assert.Equal(24, pixels.GetPixel(0, 45).ToColor()!.R);
        Assert.Equal(255, pixels.GetPixel(159, 45).ToColor()!.R);
        Assert.Equal(0, pixels.GetPixel(159, 45).ToColor()!.G);
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateLinkAsync(viewer, applied.Id, Ct));
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
