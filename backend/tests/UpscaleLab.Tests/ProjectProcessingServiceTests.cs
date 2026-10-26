using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Processing;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ProjectProcessingServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static void Original(WorkflowStore store)
    {
        using var image = new MagickImage(MagickColors.Blue, 10, 10);
        store.Files["source.png"] = image.ToByteArray(MagickFormat.Png);
    }

    [Fact]
    public async Task Upscale_PersistsVerifiedResultAndPreservesExistingLayers()
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        project.Layers.Add(new ImageLayer { ObjectKey = "existing.png", LayerType = LayerType.Background });
        db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); Original(store);
        var processor = new TestProcessor();
        var service = new ProjectProcessingService(db, store, processor, new ProcessingJobQueue(), NullLogger<ProjectProcessingService>.Instance);
        var started = await service.StartAsync(project.UserId, project.Id, new(ProcessingOperation.Upscale, 2), Ct);
        await service.ExecuteAsync(started.JobId, Ct);
        var status = await service.GetStatusAsync(project.UserId, project.Id, Ct);
        Assert.Equal(ProcessingJobStatus.Completed, status.Status);
        Assert.Equal(20, status.ResultWidth);
        Assert.True(status.HasImageResult);
        Assert.Equal("existing.png", (await db.ImageLayers.SingleAsync()).ObjectKey);
        Assert.NotEmpty((await service.GetResultUrlAsync(project.UserId, project.Id, started.JobId, Ct)).Url);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetResultUrlAsync(Guid.NewGuid(), project.Id, started.JobId, Ct));
    }

    [Fact]
    public async Task InvalidCloudOutput_FailsWithoutReplacingPreviousLayersOrLeavingResultFiles()
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        project.Layers.Add(new ImageLayer { ObjectKey = "existing.png", LayerType = LayerType.Background });
        db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); Original(store);
        var processor = new TestProcessor { WrongDimensions = true };
        var service = new ProjectProcessingService(db, store, processor, new ProcessingJobQueue(), NullLogger<ProjectProcessingService>.Instance);
        var job = await service.StartAsync(project.UserId, project.Id, new(ProcessingOperation.Upscale, 2), Ct);
        await service.ExecuteAsync(job.JobId, Ct);
        var result = await db.ProcessingJobs.SingleAsync();
        Assert.Equal(ProcessingJobStatus.Failed, result.Status);
        Assert.Null(result.ResultObjectKey);
        Assert.Equal("existing.png", (await db.ImageLayers.SingleAsync()).ObjectKey);
        Assert.Single(store.Files);
    }

    [Fact]
    public async Task UnconnectedCloudProvider_RejectsBeforeCreatingAJob()
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid()); db.Add(project); await db.SaveChangesAsync();
        var service = new ProjectProcessingService(db, new WorkflowStore(), new TestProcessor { Enabled = false },
            new ProcessingJobQueue(), NullLogger<ProjectProcessingService>.Instance);
        await Assert.ThrowsAsync<ConfigurationException>(() => service.StartAsync(project.UserId, project.Id, new(ProcessingOperation.Upscale), Ct));
        Assert.Empty(await db.ProcessingJobs.ToListAsync());
    }

    private sealed class TestProcessor : IProjectImageProcessor
    {
        public bool WrongDimensions { get; init; }
        public bool Enabled { get; init; } = true;
        public bool Supports(ProcessingOperation operation) => Enabled;
        public Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken cancellationToken)
        {
            var path = Path.Combine(request.OutputDirectory, "upscaled.png");
            var size = WrongDimensions ? 10 : 20;
            using var image = new MagickImage(MagickColors.Red, (uint)size, (uint)size);
            image.Write(path, MagickFormat.Png);
            return Task.FromResult(new ProjectProcessingResult([], path, size, size));
        }
    }
}
