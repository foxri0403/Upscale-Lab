using ImageMagick;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Projects;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Projects;
using UpscaleLab.Infrastructure.Wallpapers;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ProjectEffectResetTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static async Task<LiveLayerProject> Seed(ApplicationDbContext db, WorkflowStore store)
    {
        var project = WorkflowData.Project(Guid.NewGuid());
        project.Status = ProjectStatus.Completed;
        project.SavedEditJson = ImageEditState.Create(new(35, "warm", "wide", 2, true, Tags: ["풍경"], LiveEnabled: true)).ToJson();
        using var original = new MagickImage(MagickColors.Red, 10, 10);
        using var enlarged = new MagickImage(MagickColors.Blue, 20, 20);
        using var transparent = new MagickImage(MagickColors.Transparent, 20, 20);
        store.Files["source.png"] = original.ToByteArray(MagickFormat.Png);
        store.Files["up.png"] = enlarged.ToByteArray(MagickFormat.Png);
        store.Files["bg.png"] = enlarged.ToByteArray(MagickFormat.Png);
        store.Files["subject.png"] = transparent.ToByteArray(MagickFormat.Png);
        project.ProcessingJobs.Add(new() { Project = project, Operation = ProcessingOperation.Upscale, Status = ProcessingJobStatus.Completed,
            Progress = 100, ResultObjectKey = "up.png", ResultWidth = 20, ResultHeight = 20, CompletedAt = DateTime.UtcNow });
        project.ProcessingJobs.Add(new() { Project = project, Operation = ProcessingOperation.Layers, Status = ProcessingJobStatus.Completed,
            Progress = 100, ResultWidth = 20, ResultHeight = 20, CreatedAt = DateTime.UtcNow.AddSeconds(1), CompletedAt = DateTime.UtcNow.AddSeconds(1) });
        project.Layers.Add(new() { Project = project, ObjectKey = "bg.png", ImageUrl = "s3://test/bg.png", LayerType = LayerType.Background, LayerOrder = 0 });
        project.Layers.Add(new() { Project = project, ObjectKey = "subject.png", ImageUrl = "s3://test/subject.png", LayerType = LayerType.Body, LayerOrder = 1,
            MovementX = 4, MovementY = 6, PositionX = 2, PositionY = 4 });
        db.Add(project); await db.SaveChangesAsync(); return project;
    }

    [Fact]
    public async Task FullResetRestoresOriginalAndDefaultEditsWhileExistingWallpaperRemainsImmutable()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); var p = await Seed(db, store);
        var original = store.Files["source.png"].ToArray(); var wallpapers = new WallpaperService(db, store, store);
        var frozen = await wallpapers.CreateAsync(p.UserId, new(p.Id, "Snapshot", WallpaperMode.Live2D), Ct);
        var frozenManifest = (await db.WallpaperAssets.SingleAsync()).ManifestJson;
        var result = await new ProjectService(db, store).ResetEffectsAsync(p.UserId, p.Id, new(), Ct);
        Assert.Null(result.ImageResult); Assert.Empty(result.Layers); Assert.Empty(result.Features!);
        Assert.Null(result.LatestJob); Assert.Equal(ProjectStatus.Uploaded, result.Status);
        Assert.Equal("original", result.Edit!.Filter); Assert.Equal("original", result.Edit.Crop);
        Assert.Equal(1, result.Edit.Scale); Assert.False(result.Edit.LiveEnabled); Assert.Equal(70, result.Edit.Strength);
        Assert.Equal(new[] { "풍경" }, result.Edit.Tags); Assert.Equal(original, store.Files["source.png"]);
        Assert.DoesNotContain("up.png", store.Files.Keys); Assert.DoesNotContain("bg.png", store.Files.Keys);
        Assert.Equal(frozenManifest, (await db.WallpaperAssets.SingleAsync()).ManifestJson);
        Assert.Equal(2, (await wallpapers.GetManifestAsync(p.UserId, frozen.Id, false, Ct)).Files.Count);
        db.ChangeTracker.Clear(); Assert.Null((await new ProjectService(db, store).GetAsync(p.UserId, p.Id, Ct)).ImageResult);
    }

    [Fact]
    public async Task UpscaleCancellationPreservesSeparationAlphaAndMotionOnOriginalCanvas()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); var p = await Seed(db, store);
        var result = await new ProjectService(db, store).ResetEffectsAsync(p.UserId, p.Id, new("Upscale"), Ct);
        Assert.Null(result.ImageResult); Assert.Equal(2, result.Layers.Count);
        Assert.Equal(new[] { "배경 분리", "Live 2D" }, result.Features);
        Assert.Equal("warm", result.Edit!.Filter); Assert.True(result.Edit.LiveEnabled); Assert.Equal(1, result.Edit.Scale);
        var subject = await db.ImageLayers.SingleAsync(x => x.LayerType == LayerType.Body);
        Assert.Equal(2, subject.MovementX); Assert.Equal(3, subject.MovementY); Assert.Equal(1, subject.PositionX);
        using var pixels = new MagickImage(store.Files[subject.ObjectKey]); Assert.Equal(10u, pixels.Width); Assert.True(pixels.HasAlpha);
        Assert.DoesNotContain("up.png", store.Files.Keys); Assert.Equal(3, store.Files.Count);
        var wallpapers = new WallpaperService(db, store, store);
        var asset = await wallpapers.CreateAsync(p.UserId, new(p.Id, "Original-size layers", WallpaperMode.Live2D), Ct);
        Assert.Equal(10, (await wallpapers.GetManifestAsync(p.UserId, asset.Id, false, Ct)).Width);
    }

    [Fact]
    public async Task SeparationCancellationKeepsUpscaleOtherEditsAndIndependentLive2DState()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); var p = await Seed(db, store);
        var result = await new ProjectService(db, store).ResetEffectsAsync(p.UserId, p.Id, new("Layers"), Ct);
        Assert.Empty(result.Layers); Assert.Equal(20, result.ImageResult!.Width);
        Assert.Equal(new[] { "업스케일", "Live 2D" }, result.Features); Assert.True(result.Edit!.LiveEnabled);
        Assert.Equal("warm", result.Edit.Filter); Assert.Equal("wide", result.Edit.Crop);
        Assert.Equal(new[] { "source.png", "up.png" }, store.Files.Keys.OrderBy(x => x));
    }

    [Fact]
    public async Task ResetEnforcesOwnershipPublicLocksAndProcessingState()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); var p = await Seed(db, store);
        var service = new ProjectService(db, store);
        await Assert.ThrowsAsync<NotFoundException>(() => service.ResetEffectsAsync(Guid.NewGuid(), p.Id, new(), Ct));
        await Assert.ThrowsAsync<ValidationException>(() => service.ResetEffectsAsync(p.UserId, p.Id, new("unknown"), Ct));
        var asset = new WallpaperAsset { UserId = p.UserId, SourceProjectId = p.Id, IsPublic = true }; db.Add(asset); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.ResetEffectsAsync(p.UserId, p.Id, new(), Ct));
        asset.IsPublic = false; p.ProcessingJobs.First().Status = ProcessingJobStatus.Processing; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.ResetEffectsAsync(p.UserId, p.Id, new(), Ct));
        Assert.Equal(4, store.Files.Count);
    }

    [Fact]
    public async Task DraftResetDoesNotSaveImageOrRegisterItInTheLibrary()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); var p = await Seed(db, store);
        p.SavedEditJson = ProjectDraftState.Pending; await db.SaveChangesAsync(); var service = new ProjectService(db, store);
        var result = await service.ResetEffectsAsync(p.UserId, p.Id, new(), Ct);
        Assert.True(result.IsDraft); Assert.Null(result.Edit); Assert.Empty(await service.GetAllAsync(p.UserId, Ct));
        Assert.Single(store.Files);
    }

    [Fact]
    public async Task FailedLayerResizeUploadDoesNotRemoveCurrentResults()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); var p = await Seed(db, store);
        store.FailUpload = true;
        await Assert.ThrowsAsync<IOException>(() => new ProjectService(db, store).ResetEffectsAsync(p.UserId, p.Id, new("Upscale"), Ct));
        db.ChangeTracker.Clear(); var detail = await new ProjectService(db, store).GetAsync(p.UserId, p.Id, Ct);
        Assert.NotNull(detail.ImageResult); Assert.Equal(2, detail.Layers.Count); Assert.Equal(4, store.Files.Count);
    }

    [Theory]
    [InlineData("Upscale", ProcessingOperation.Layers)]
    [InlineData("Layers", ProcessingOperation.Upscale)]
    public async Task CombinedLegacyJobRetainsTheOtherEffect(string feature, ProcessingOperation remaining)
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); var p = await Seed(db, store);
        var up = p.ProcessingJobs.Single(x => x.ResultObjectKey != null);
        var split = p.ProcessingJobs.Single(x => x.ResultObjectKey == null);
        db.ProcessingJobs.Remove(split); p.ProcessingJobs.Remove(split); up.Operation = ProcessingOperation.UpscaleAndLayers;
        await db.SaveChangesAsync();
        var result = await new ProjectService(db, store).ResetEffectsAsync(p.UserId, p.Id, new(feature), Ct);
        Assert.Equal(remaining, Assert.Single(p.ProcessingJobs).Operation);
        Assert.Equal(feature == "Upscale" ? 2 : 0, result.Layers.Count);
        Assert.Equal(feature == "Layers", result.ImageResult != null);
    }
}
