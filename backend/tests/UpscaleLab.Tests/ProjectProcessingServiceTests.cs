using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Upscaling;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Processing;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ProjectProcessingServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static void Original(WorkflowStore store, uint width = 10, uint height = 10)
    {
        using var image = new MagickImage(MagickColors.Blue, width, height);
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
        var service = new ProjectProcessingService(db, store, processor, new ProcessingJobQueue(), new FakeTokenService(), NullLogger<ProjectProcessingService>.Instance);
        var started = await service.StartAsync(project.UserId, project.Id, new(ProcessingOperation.Upscale, TargetLongEdge: 1920), Ct);
        await service.ExecuteAsync(started.JobId, Ct);
        var status = await service.GetStatusAsync(project.UserId, project.Id, Ct);
        Assert.Equal(ProcessingJobStatus.Completed, status.Status);
        Assert.Equal(1920, status.ResultWidth);
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
        var service = new ProjectProcessingService(db, store, processor, new ProcessingJobQueue(), new FakeTokenService(), NullLogger<ProjectProcessingService>.Instance);
        var job = await service.StartAsync(project.UserId, project.Id, new(ProcessingOperation.Upscale, TargetLongEdge: 1920), Ct);
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
            new ProcessingJobQueue(), new FakeTokenService(), NullLogger<ProjectProcessingService>.Instance);
        await Assert.ThrowsAsync<ConfigurationException>(() => service.StartAsync(project.UserId, project.Id, new(ProcessingOperation.Upscale), Ct));
        Assert.Empty(await db.ProcessingJobs.ToListAsync());
    }

    [Fact]
    public async Task ResolutionTargetPersistsAnExactQhdResultAndDoesNotRepeatExistingUpgrades()
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        project.OriginalImage.OriginalWidth = 1920; project.OriginalImage.OriginalHeight = 1080;
        db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); Original(store, 1920, 1080);
        var service = new ProjectProcessingService(db, store, new TestProcessor(), new ProcessingJobQueue(), new FakeTokenService(), NullLogger<ProjectProcessingService>.Instance);
        var request = new ProcessProjectRequest(ProcessingOperation.Upscale, TargetLongEdge: 2560);
        var job = await service.StartAsync(project.UserId, project.Id, request, Ct);
        Assert.Equal(2560, job.TargetLongEdge);
        await service.ExecuteAsync(job.JobId, Ct);
        var saved = await service.GetStatusAsync(project.UserId, project.Id, Ct);
        Assert.True(
            saved.Status == ProcessingJobStatus.Completed,
            $"작업 실패: {saved.ErrorMessage}");
        Assert.Equal(2560, saved.ResultWidth); Assert.Equal(1440, saved.ResultHeight);
        Assert.Equal(2560, saved.TargetLongEdge);
        await Assert.ThrowsAsync<ConflictException>(() => service.StartAsync(project.UserId, project.Id, request, Ct));
        Assert.Single(await db.ProcessingJobs.ToListAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => service.StartAsync(Guid.NewGuid(), project.Id,
            request with { TargetLongEdge = 3840 }, Ct));
    }

    [Theory]
    [InlineData(0, ProcessingOperation.Upscale)]
    [InlineData(4096, ProcessingOperation.Upscale)]
    [InlineData(1920, ProcessingOperation.Upscale)]
    [InlineData(2560, ProcessingOperation.Layers)]
    public async Task InvalidOrLowerResolutionIsRejectedBeforeAJobIsCreated(int edge, ProcessingOperation operation)
    {
        await using var db = WorkflowData.Db(); var project = WorkflowData.Project(Guid.NewGuid());
        project.OriginalImage.OriginalWidth = 1920; project.OriginalImage.OriginalHeight = 1080;
        db.Add(project); await db.SaveChangesAsync();
        var service = new ProjectProcessingService(db, new WorkflowStore(), new TestProcessor(), new ProcessingJobQueue(), new FakeTokenService(), NullLogger<ProjectProcessingService>.Instance);
        await Assert.ThrowsAsync<ValidationException>(() => service.StartAsync(project.UserId, project.Id,
            new(operation, TargetLongEdge: edge), Ct));
        Assert.Empty(await db.ProcessingJobs.ToListAsync());
    }

    [Fact]
    public async Task ACloudResultThatMissesTheSelectedResolutionIsNotSaved()
    {
        await using var db = WorkflowData.Db(); var project = WorkflowData.Project(Guid.NewGuid());
        db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); Original(store);
        var service = new ProjectProcessingService(db, store, new TestProcessor { WrongDimensions = true }, new ProcessingJobQueue(), new FakeTokenService(), NullLogger<ProjectProcessingService>.Instance);
        var job = await service.StartAsync(project.UserId, project.Id, new(ProcessingOperation.Upscale, TargetLongEdge: 1920), Ct);
        await service.ExecuteAsync(job.JobId, Ct);
        Assert.Equal(ProcessingJobStatus.Failed, (await service.GetStatusAsync(project.UserId, project.Id, Ct)).Status);
        Assert.Single(store.Files);
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
            var dimensions = (Width: size, Height: size);
            if (request.TargetLongEdge is int edge && !WrongDimensions)
            {
                var info = new MagickImageInfo(request.InputPath);
                dimensions = UpscaleResolution.Dimensions((int)info.Width, (int)info.Height, edge);
            }
            using var image = new MagickImage(MagickColors.Red, (uint)dimensions.Width, (uint)dimensions.Height);
            image.Write(path, MagickFormat.Png);
            return Task.FromResult(new ProjectProcessingResult([], path, dimensions.Width, dimensions.Height));
        }
    }
}
