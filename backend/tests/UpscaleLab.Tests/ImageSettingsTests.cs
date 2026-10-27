using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Projects;
using UpscaleLab.Infrastructure.Wallpapers;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ImageSettingsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectRename_RequiresOwnershipAndAllSharingOff(bool legacyGallery)
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        db.Add(project); await db.SaveChangesAsync();
        var previousTime = project.UpdatedAt;
        var service = new ProjectService(db, new WorkflowStore());
        await Assert.ThrowsAsync<NotFoundException>(() => service.RenameAsync(
            Guid.NewGuid(), project.Id, new("Other account"), Ct));
        if (legacyGallery)
            db.GalleryPosts.Add(new GalleryPost { UserId = project.UserId, ProjectId = project.Id,
                ImageId = project.OriginalImageId, Title = "Public", IsPublic = true });
        else
            db.WallpaperAssets.Add(new WallpaperAsset { UserId = project.UserId,
                SourceProjectId = project.Id, Title = "Public", IsPublic = true });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.RenameAsync(
            project.UserId, project.Id, new("Blocked"), Ct));
        Assert.Equal("Source", project.Title);
        foreach (var post in db.GalleryPosts) post.IsPublic = false;
        foreach (var asset in db.WallpaperAssets) asset.IsPublic = false;
        await db.SaveChangesAsync();
        await service.RenameAsync(project.UserId, project.Id, new("  Renamed  "), Ct);
        Assert.Equal("Renamed", (await db.LiveLayerProjects.AsNoTracking().SingleAsync()).Title);
        Assert.True(project.UpdatedAt >= previousTime);
    }

    [Fact]
    public async Task WallpaperRename_ChangesOnlyPrivateOwnedMetadataAndKeepsTheManifest()
    {
        await using var db = WorkflowData.Db();
        var store = new WorkflowStore(); store.Files["source.png"] = [1, 2];
        var project = WorkflowData.Project(Guid.NewGuid());
        db.Add(project); await db.SaveChangesAsync();
        var service = new WallpaperService(db, store, store);
        var asset = await service.CreateAsync(project.UserId, new(project.Id, "Saved", WallpaperMode.Static), Ct);
        var manifest = (await db.WallpaperAssets.SingleAsync()).ManifestJson;
        await Assert.ThrowsAsync<NotFoundException>(() => service.RenameAsync(
            Guid.NewGuid(), asset.Id, new("Other account"), Ct));
        await service.SetSharingAsync(project.UserId, asset.Id, true, Ct);
        await Assert.ThrowsAsync<ConflictException>(() => service.RenameAsync(
            project.UserId, asset.Id, new("Blocked"), Ct));
        await service.SetSharingAsync(project.UserId, asset.Id, false, Ct);
        await service.RenameAsync(project.UserId, asset.Id, new("  Renamed  "), Ct);
        var saved = await db.WallpaperAssets.AsNoTracking().SingleAsync();
        Assert.Equal("Renamed", saved.Title);
        Assert.Equal(manifest, saved.ManifestJson);
        Assert.Equal("Source", project.Title);
    }

    [Fact]
    public async Task Rename_RejectsEmptyAndOverlongNamesWithoutChangingSavedMetadata()
    {
        await using var db = WorkflowData.Db();
        var store = new WorkflowStore(); store.Files["source.png"] = [1];
        var project = WorkflowData.Project(Guid.NewGuid());
        db.Add(project); await db.SaveChangesAsync();
        var projects = new ProjectService(db, store);
        var wallpapers = new WallpaperService(db, store, store);
        var asset = await wallpapers.CreateAsync(project.UserId, new(project.Id, "Saved", WallpaperMode.Static), Ct);
        foreach (var name in new[] { "", "  ", new string('x', 161) })
        {
            await Assert.ThrowsAsync<ValidationException>(() => projects.RenameAsync(project.UserId, project.Id, new(name), Ct));
            await Assert.ThrowsAsync<ValidationException>(() => wallpapers.RenameAsync(project.UserId, asset.Id, new(name), Ct));
        }
        Assert.Equal("Source", project.Title);
        Assert.Equal("Saved", (await db.WallpaperAssets.SingleAsync()).Title);
    }

    [Fact]
    public async Task RenderedSnapshot_TracksItsOwnedOriginWithoutExposingItInThePublicFeed()
    {
        await using var db = WorkflowData.Db();
        var store = new WorkflowStore(); store.Files["source.png"] = [1];
        var source = WorkflowData.Project(Guid.NewGuid());
        var rendered = WorkflowData.Project(source.UserId);
        var other = WorkflowData.Project(Guid.NewGuid());
        db.AddRange(source, rendered, other); await db.SaveChangesAsync();
        var service = new WallpaperService(db, store, store);
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(source.UserId,
            new(rendered.Id, "Invalid origin", WallpaperMode.Static, OriginProjectId: other.Id), Ct));
        Assert.Empty(await db.WallpaperAssets.ToListAsync());
        var asset = await service.CreateAsync(source.UserId,
            new(rendered.Id, "Rendered", WallpaperMode.Static, OriginProjectId: source.Id), Ct);
        Assert.Equal(source.Id, asset.SourceProjectId);
        await service.SetSharingAsync(source.UserId, asset.Id, true, Ct);
        Assert.Equal(source.Id, (await service.ListAsync(source.UserId, false, Ct)).Single().SourceProjectId);
        Assert.Null((await service.ListAsync(null, true, Ct)).Single().SourceProjectId);
        await Assert.ThrowsAsync<ConflictException>(() => new ProjectService(db, store).RenameAsync(
            source.UserId, source.Id, new("Blocked by rendered snapshot"), Ct));
    }
}
