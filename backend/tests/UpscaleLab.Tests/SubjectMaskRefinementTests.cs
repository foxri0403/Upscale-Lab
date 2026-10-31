using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Projects;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class SubjectMaskRefinementTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static LiveLayerProject Project()
    {
        var project = WorkflowData.Project(Guid.NewGuid());
        project.Layers.Add(new() { LayerType = LayerType.Background, LayerOrder = 0, ObjectKey = "old-background.png" });
        project.Layers.Add(new() { LayerType = LayerType.Body, LayerOrder = 1, ObjectKey = "old-subject.png" });
        return project;
    }
    private static MemoryStream Mask(uint width = 10, uint height = 10, bool opaque = true, bool all = false, bool empty = false)
    {
        using var image = new MagickImage(opaque ? MagickColors.Black : MagickColors.Transparent, width, height);
        if (all) image.BackgroundColor = MagickColors.White;
        using var subject = new MagickImage(MagickColors.White, all ? width : width / 2, height);
        if (!empty) image.Composite(subject, 0, 0, CompositeOperator.Over);
        // A soft edge must survive upload, recovery and delivery to the processor.
        if (!all && !empty) { using var edge = new MagickImage(new MagickColor(128, 128, 128), 1, height);
            image.Composite(edge, (int)(width / 2 - 1), 0, CompositeOperator.Over); }
        return new MemoryStream(image.ToByteArray(MagickFormat.Png));
    }
    private static ProjectProcessingService Service(UpscaleLab.Infrastructure.Database.ApplicationDbContext db,
        WorkflowStore store, CaptureProcessor processor, ProcessingJobQueue? queue = null, FakeTokenService? tokens = null) =>
        new(db, WorkflowData.Factory, store, processor, queue ?? new(), tokens ?? new FakeTokenService(), NullLogger<ProjectProcessingService>.Instance);

    [Fact]
    public async Task FirstManualSelectionCreatesLayersAndCannotOverwriteExistingLayersWithEmptyBase()
    {
        await using var db = WorkflowData.Db(); var project = WorkflowData.Project(Guid.NewGuid());
        db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); using var source = new MagickImage(MagickColors.Red, 10, 10);
        store.Files["source.png"] = source.ToByteArray(MagickFormat.Png);
        var processor = new CaptureProcessor(); var tokens = new FakeTokenService(); var service = Service(db, store, processor, tokens: tokens);
        using var mask = Mask();
        var job = await service.RefineSubjectAsync(project.UserId, project.Id, Guid.Empty, mask, Ct);
        await service.ExecuteAsync(job.JobId, Ct);
        Assert.Equal(ProcessingJobStatus.Completed, (await service.GetStatusAsync(project.UserId, project.Id, Ct)).Status);
        Assert.Equal(2, await db.ImageLayers.CountAsync()); Assert.Equal(128, processor.SoftAlpha);
        Assert.Equal(0, tokens.ReservedCount); Assert.Equal(0, tokens.CompletedCount); Assert.Equal(0, tokens.RefundedCount);
        using var staleMask = Mask();
        await Assert.ThrowsAsync<ConflictException>(() => service.RefineSubjectAsync(project.UserId, project.Id, Guid.Empty, staleMask, Ct));
    }

    [Fact]
    public async Task RefinementSurvivesRecoveryAndUsesLatestUpscaledSourceWithExactSoftMask()
    {
        await using var db = WorkflowData.Db(); var project = Project();
        project.ProcessingJobs.Add(new() { Operation = ProcessingOperation.Upscale, Status = ProcessingJobStatus.Completed,
            ResultObjectKey = "upscaled.png", ResultWidth = 20, ResultHeight = 20 });
        db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore();
        using var original = new MagickImage(MagickColors.Blue, 10, 10);
        using var upscaled = new MagickImage(MagickColors.Red, 20, 20);
        store.Files["source.png"] = original.ToByteArray(MagickFormat.Png);
        store.Files["upscaled.png"] = upscaled.ToByteArray(MagickFormat.Png);
        store.Files["old-background.png"] = [1]; store.Files["old-subject.png"] = [2];
        var processor = new CaptureProcessor(); using var mask = Mask(20, 20);
        var started = await Service(db, store, processor).RefineSubjectAsync(project.UserId, project.Id, project.Layers.Last().Id, mask, Ct);
        var key = (await db.ProcessingJobs.SingleAsync(x => x.Id == started.JobId)).SubjectMaskObjectKey!;
        Assert.Contains($"users/{project.UserId:N}/projects/{project.Id:N}/masks/", key);
        var queue = new ProcessingJobQueue(); db.ChangeTracker.Clear();
        var recovered = Service(db, store, processor, queue);
        await recovered.RecoverPendingAsync(Ct);
        Assert.Equal(started.JobId, await queue.DequeueAsync(Ct));
        await recovered.ExecuteAsync(started.JobId, Ct);
        Assert.Equal(ProcessingJobStatus.Completed, (await recovered.GetStatusAsync(project.UserId, project.Id, Ct)).Status);
        Assert.Equal(20u, processor.InputWidth); Assert.Equal(128, processor.SoftAlpha);
        Assert.Equal(2, await db.ImageLayers.CountAsync());
        Assert.DoesNotContain("old-background.png", store.Files.Keys); Assert.DoesNotContain("old-subject.png", store.Files.Keys);
        Assert.Contains("upscaled.png", store.Files.Keys);
        await new ProjectService(db, store).DeleteAsync(project.UserId, project.Id, Ct);
        Assert.Empty(store.Files);
    }

    [Fact]
    public async Task FailedRefinementPreservesPreviousLayersAndCanBeRetried()
    {
        await using var db = WorkflowData.Db(); var project = Project(); db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); using var source = new MagickImage(MagickColors.Red, 10, 10);
        store.Files["source.png"] = source.ToByteArray(MagickFormat.Png);
        var processor = new CaptureProcessor { Fail = true }; var service = Service(db, store, processor);
        var layerId = project.Layers.Last().Id; using var mask = Mask();
        var job = await service.RefineSubjectAsync(project.UserId, project.Id, layerId, mask, Ct);
        await service.ExecuteAsync(job.JobId, Ct);
        Assert.Equal(ProcessingJobStatus.Failed, (await service.GetStatusAsync(project.UserId, project.Id, Ct)).Status);
        Assert.Equal(layerId, (await db.ImageLayers.SingleAsync(x => x.LayerType == LayerType.Body)).Id);
        using var retryMask = Mask();
        Assert.Equal(ProcessingJobStatus.Queued, (await service.RefineSubjectAsync(project.UserId, project.Id, layerId, retryMask, Ct)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnershipStaleResultsPublishedProjectsAndConcurrentJobsAreRejectedBeforeStorage(bool selection)
    {
        await using var db = WorkflowData.Db(); var project = Project(); db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); var service = Service(db, store, new()); using var mask = Mask();
        var layerId = project.Layers.Last().Id;
        Task Refine(Guid user, Guid layer) => selection ? service.RefineSelectionAsync(user, project.Id, layer, mask, Ct) : service.RefineSubjectAsync(user, project.Id, layer, mask, Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => Refine(Guid.NewGuid(), layerId));
        await Assert.ThrowsAsync<ConflictException>(() => Refine(project.UserId, Guid.NewGuid()));
        var post = new GalleryPost { ProjectId = project.Id, UserId = project.UserId, ImageId = project.OriginalImageId, IsPublic = true };
        db.Add(post); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => Refine(project.UserId, layerId));
        db.Remove(post); db.Add(new ProcessingJob { ProjectId = project.Id }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => Refine(project.UserId, layerId));
        Assert.Empty(store.Files);
    }

    [Theory]
    [InlineData("dimensions")]
    [InlineData("transparent")]
    [InlineData("full")]
    [InlineData("empty")]
    [InlineData("corrupt")]
    public async Task InvalidMasksNeverCreateAJobOrAnObject(string kind)
    {
        await using var db = WorkflowData.Db(); var project = Project(); db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); var service = Service(db, store, new());
        using var mask = kind == "corrupt" ? new MemoryStream([1, 2, 3])
            : Mask(kind == "dimensions" ? 20u : 10u, opaque: kind != "transparent", all: kind == "full", empty: kind == "empty");
        await Assert.ThrowsAsync<ValidationException>(() => service.RefineSubjectAsync(project.UserId, project.Id, project.Layers.Last().Id, mask, Ct));
        Assert.Empty(await db.ProcessingJobs.ToListAsync()); Assert.Empty(store.Files);
    }

    [Fact]
    public async Task SelectionUsesTrustedNativeAlphaAndPreservesModeAcrossQueueRecovery()
    {
        await using var db = WorkflowData.Db(); var project = Project(); db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); using var image = new MagickImage(MagickColors.Blue, 10, 10);
        store.Files["source.png"] = image.ToByteArray(MagickFormat.Png);
        using var alphaStream = Mask(); using var alpha = new MagickImage(alphaStream);
        image.Alpha(AlphaOption.Set); image.Composite(alpha, CompositeOperator.CopyAlpha);
        store.Files["old-subject.png"] = image.ToByteArray(MagickFormat.Png);
        using var hints = new MagickImage(MagickColors.Red, 10, 10);
        using var erase = new MagickImage(MagickColors.Lime, 1, 10); hints.Composite(erase, 0, 0, CompositeOperator.Over);
        using var input = new MemoryStream(hints.ToByteArray(MagickFormat.Png));
        var processor = new CaptureProcessor(); var started = await Service(db, store, processor)
            .RefineSelectionAsync(project.UserId, project.Id, project.Layers.Last().Id, input, Ct);
        var key = (await db.ProcessingJobs.SingleAsync()).SubjectMaskObjectKey!;
        Assert.EndsWith(".selection.png", key);
        using var packed = new MagickImage(store.Files[key]); using var pixels = packed.GetPixels();
        Assert.Equal(128, pixels.GetPixel(4, 0).ToColor()!.R);
        Assert.Equal(255, pixels.GetPixel(4, 0).ToColor()!.G);
        Assert.Equal(0, pixels.GetPixel(4, 0).ToColor()!.B);
        Assert.Equal(255, pixels.GetPixel(0, 0).ToColor()!.B);
        Assert.Equal(0, pixels.GetPixel(9, 0).ToColor()!.R);
        db.ChangeTracker.Clear(); var queue = new ProcessingJobQueue(); var service = Service(db, store, processor, queue);
        await service.RecoverPendingAsync(Ct); Assert.Equal(started.JobId, await queue.DequeueAsync(Ct));
        await service.ExecuteAsync(started.JobId, Ct);
        Assert.True(processor.Selection); Assert.Equal(128, processor.SoftAlpha);
        Assert.Equal(ProcessingJobStatus.Completed, (await service.GetStatusAsync(project.UserId, project.Id, Ct)).Status);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("blue")]
    [InlineData("transparent")]
    [InlineData("dimensions")]
    public async Task InvalidSelectionNeverCreatesJobOrUploadsObject(string kind)
    {
        await using var db = WorkflowData.Db(); var project = Project(); db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); using var prior = new MagickImage(MagickColors.Transparent, 10, 10);
        using var body = new MagickImage(MagickColors.White, 5, 10); prior.Composite(body, 0, 0, CompositeOperator.Over);
        store.Files["old-subject.png"] = prior.ToByteArray(MagickFormat.Png);
        using var hints = new MagickImage(kind == "empty" ? MagickColors.Black : kind == "blue" ? MagickColors.Blue : kind == "transparent" ? MagickColors.Transparent : MagickColors.Red,
            kind == "dimensions" ? 20u : 10u, 10);
        using var input = new MemoryStream(hints.ToByteArray(MagickFormat.Png));
        await Assert.ThrowsAsync<ValidationException>(() => Service(db, store, new()).RefineSelectionAsync(project.UserId, project.Id, project.Layers.Last().Id, input, Ct));
        Assert.Empty(await db.ProcessingJobs.ToListAsync()); Assert.Single(store.Files);
    }

    private sealed class CaptureProcessor : IProjectImageProcessor
    {
        public bool Fail; public uint InputWidth; public byte SoftAlpha; public bool Selection;
        public bool Supports(ProcessingOperation operation) => operation == ProcessingOperation.Layers;
        public Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken ct)
        {
            if (Fail) throw new IOException("Simulated worker failure");
            Selection = request.RefineSelection;
            using var source = new MagickImage(request.InputPath); InputWidth = source.Width;
            using var mask = new MagickImage(request.SubjectMaskPath!);
            using var pixels = mask.GetPixels(); SoftAlpha = pixels.GetPixel((int)(mask.Width / 2 - 1), 0).ToColor()!.R;
            var background = Path.Combine(request.OutputDirectory, "background.png");
            var foreground = Path.Combine(request.OutputDirectory, "subject.png");
            source.Write(background, MagickFormat.Png);
            source.Alpha(AlphaOption.Set); source.Composite(mask, CompositeOperator.CopyAlpha); source.Write(foreground, MagickFormat.Png);
            return Task.FromResult(new ProjectProcessingResult([
                new(background, LayerType.Background, 0, 0, 0, 0, 0, 1, 0, 0),
                new(foreground, LayerType.Body, 1, 1, 0, 0, 0, 1, 1, 1)], null, (int)source.Width, (int)source.Height));
        }
    }
}
