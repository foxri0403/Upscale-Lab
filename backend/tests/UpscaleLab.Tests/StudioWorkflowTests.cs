using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Projects;
using UpscaleLab.Infrastructure.Wallpapers;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class StudioWorkflowTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task TitleAndTagsSaveOnOneProjectAndAppearOnTheUploadedSnapshot()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore();
        var project = WorkflowData.Project(Guid.NewGuid()); db.Add(project); await db.SaveChangesAsync();
        store.Files["source.png"] = [1];
        var projects = new ProjectService(db, store);
        var request = new SaveImageEditRequest(35, "original", "original", 1, true, "  새 이름  ", ["풍경", "게임", "풍경"]);
        await projects.SaveEditAsync(project.UserId, project.Id, request, Ct);
        await projects.SaveEditAsync(project.UserId, project.Id, request with { Tags = ["동물"] }, Ct);
        db.ChangeTracker.Clear();
        var loaded = await projects.GetAsync(project.UserId, project.Id, Ct);
        Assert.Equal("새 이름", loaded.Title); Assert.Equal(new[] { "동물" }, loaded.Edit!.Tags);
        Assert.Single(await db.LiveLayerProjects.ToListAsync()); Assert.Single(store.Files);
        var wallpapers = new WallpaperService(db, store, store);
        var asset = await wallpapers.CreateAsync(project.UserId, new(project.Id, loaded.Title, WallpaperMode.Static), Ct);
        await wallpapers.SetSharingAsync(project.UserId, asset.Id, true, Ct);
        Assert.Equal(new[] { "동물" }, Assert.Single(await wallpapers.ListAsync(null, true, Ct)).Edit!.Tags);
        await Assert.ThrowsAsync<ConflictException>(() => projects.SaveEditAsync(project.UserId, project.Id, request, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => projects.SaveEditAsync(Guid.NewGuid(), project.Id, request, Ct));
    }

    [Fact]
    public async Task LinkSharingIsAnonymousStableAndSeparateFromExplorePublication()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); store.Files["source.png"] = [1];
        var project = WorkflowData.Project(Guid.NewGuid()); db.Add(project); await db.SaveChangesAsync();
        var service = new WallpaperService(db, store, store);
        var asset = await service.CreateAsync(project.UserId, new(project.Id, "Link only", WallpaperMode.Static), Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateLinkAsync(Guid.NewGuid(), asset.Id, Ct));
        var link = await service.CreateLinkAsync(project.UserId, asset.Id, Ct);
        Assert.Equal(64, link.Token.Length);
        Assert.Equal(link, await service.CreateLinkAsync(project.UserId, asset.Id, Ct));
        Assert.False((await db.WallpaperAssets.SingleAsync()).IsPublic);
        Assert.Empty(await service.ListAsync(null, true, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetManifestAsync(null, asset.Id, false, Ct));
        Assert.Equal("Link only", (await service.GetLinkedAsync(link.Token, Ct)).Title);
        Assert.StartsWith("https://storage.test/", (await service.GetLinkedAsync(link.Token, Ct)).Manifest.Files[0].Source);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetLinkedAsync(new string('0', 64), Ct));
        await service.SetSharingAsync(project.UserId, asset.Id, true, Ct);
        Assert.Single(await service.ListAsync(null, true, Ct));
        await service.SetSharingAsync(project.UserId, asset.Id, false, Ct);
        Assert.Equal("Link only", (await service.GetLinkedAsync(link.Token, Ct)).Title);
        await service.ArchiveAsync(project.UserId, asset.Id, Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetLinkedAsync(link.Token, Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothProcessingOrdersPreserveUpscaleSeparationAnimationAndDeviceFiles(bool layersFirst)
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore();
        var project = WorkflowData.Project(Guid.NewGuid()); db.Add(project); await db.SaveChangesAsync();
        using (var image = new MagickImage(MagickColors.Blue, 10, 10)) store.Files["source.png"] = image.ToByteArray(MagickFormat.Png);
        var processor = new PipelineProcessor();
        var processing = new ProjectProcessingService(db, store, processor, new ProcessingJobQueue(), NullLogger<ProjectProcessingService>.Instance);
        foreach (var operation in layersFirst ? new[] { ProcessingOperation.Layers, ProcessingOperation.Upscale } : new[] { ProcessingOperation.Upscale, ProcessingOperation.Layers })
        {
            var job = await processing.StartAsync(project.UserId, project.Id, new(operation, 2), Ct);
            await processing.ExecuteAsync(job.JobId, Ct);
            Assert.Equal(ProcessingJobStatus.Completed, (await processing.GetStatusAsync(project.UserId, project.Id, Ct)).Status);
        }
        var projects = new ProjectService(db, store);
        await projects.SaveEditAsync(project.UserId, project.Id, new(45, "original", "original", 1, true, LiveEnabled: true), Ct);
        db.ChangeTracker.Clear();
        var detail = await projects.GetAsync(project.UserId, project.Id, Ct);
        Assert.Equal(20, detail.ImageResult!.Width);
        Assert.Equal(new[] { "업스케일", "배경 분리", "Live 2D" }, detail.Features);
        Assert.Equal(2, detail.Layers.Count);
        foreach (var layer in await db.ImageLayers.AsNoTracking().ToArrayAsync())
        {
            var info = new MagickImageInfo(store.Files[layer.ObjectKey]); Assert.Equal(20u, info.Width);
            if (layer.LayerType != LayerType.Background) { using var image = new MagickImage(store.Files[layer.ObjectKey]); Assert.True(image.HasAlpha); }
        }
        if (!layersFirst) Assert.Equal(20u, processor.SeparationInputWidth);
        var wallpapers = new WallpaperService(db, store, store);
        var asset = await wallpapers.CreateAsync(project.UserId, new(project.Id, "All features", WallpaperMode.Live2D), Ct);
        var manifest = await wallpapers.GetManifestAsync(project.UserId, asset.Id, false, Ct);
        Assert.Equal(20, manifest.Width); Assert.Equal(2, manifest.Files.Count);
        Assert.Equal(new[] { "업스케일", "배경 분리", "Live 2D" }, asset.Features);
    }

    private sealed class PipelineProcessor : IProjectImageProcessor
    {
        public uint SeparationInputWidth;
        public bool Supports(ProcessingOperation operation) => true;
        public Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken ct)
        {
            var info = new MagickImageInfo(request.InputPath);
            var edge = request.Operation == ProcessingOperation.Upscale ? info.Width * 2 : info.Width;
            using var background = new MagickImage(MagickColors.Blue, edge, edge);
            var path = Path.Combine(request.OutputDirectory, "background.png"); background.Write(path, MagickFormat.Png);
            if (request.Operation == ProcessingOperation.Upscale) return Task.FromResult(new ProjectProcessingResult([], path, (int)edge, (int)edge));
            SeparationInputWidth = info.Width;
            var foreground = Path.Combine(request.OutputDirectory, "foreground.png");
            using var transparent = new MagickImage(MagickColors.Transparent, edge, edge); transparent.Write(foreground, MagickFormat.Png);
            return Task.FromResult(new ProjectProcessingResult([
                new(path, LayerType.Background, 0, 0, 0, 0, 0, 1, 0, 0),
                new(foreground, LayerType.Body, 1, 1, 0, 0, 0, 1, 2, 2)], null, (int)edge, (int)edge));
        }
    }
}
