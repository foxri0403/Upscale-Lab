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
        WorkflowStore store, CaptureProcessor processor, ProcessingJobQueue? queue = null) =>
        new(db, store, processor, queue ?? new(), NullLogger<ProjectProcessingService>.Instance);

    [Fact]
    public async Task FirstManualSelectionCreatesLayersAndCannotOverwriteExistingLayersWithEmptyBase()
    {
        await using var db = WorkflowData.Db(); var project = WorkflowData.Project(Guid.NewGuid());
        db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); using var source = new MagickImage(MagickColors.Red, 10, 10);
        store.Files["source.png"] = source.ToByteArray(MagickFormat.Png);
        var processor = new CaptureProcessor(); var service = Service(db, store, processor);
        using var mask = Mask();
        var job = await service.RefineSubjectAsync(project.UserId, project.Id, Guid.Empty, mask, Ct);
        await service.ExecuteAsync(job.JobId, Ct);
        Assert.Equal(ProcessingJobStatus.Completed, (await service.GetStatusAsync(project.UserId, project.Id, Ct)).Status);
        Assert.Equal(2, await db.ImageLayers.CountAsync()); Assert.Equal(128, processor.SoftAlpha);
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

    [Fact]
    public async Task OwnershipStaleResultsPublishedProjectsAndConcurrentJobsAreRejectedBeforeStorage()
    {
        await using var db = WorkflowData.Db(); var project = Project(); db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); var service = Service(db, store, new()); using var mask = Mask();
        var layerId = project.Layers.Last().Id;
        await Assert.ThrowsAsync<NotFoundException>(() => service.RefineSubjectAsync(Guid.NewGuid(), project.Id, layerId, mask, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => service.RefineSubjectAsync(project.UserId, project.Id, Guid.NewGuid(), mask, Ct));
        var post = new GalleryPost { ProjectId = project.Id, UserId = project.UserId, ImageId = project.OriginalImageId, IsPublic = true };
        db.Add(post); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.RefineSubjectAsync(project.UserId, project.Id, layerId, mask, Ct));
        db.Remove(post); db.Add(new ProcessingJob { ProjectId = project.Id }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => service.RefineSubjectAsync(project.UserId, project.Id, layerId, mask, Ct));
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

    private sealed class CaptureProcessor : IProjectImageProcessor
    {
        public bool Fail; public uint InputWidth; public byte SoftAlpha;
        public bool Supports(ProcessingOperation operation) => operation == ProcessingOperation.Layers;
        public Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken ct)
        {
            if (Fail) throw new IOException("Simulated worker failure");
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
