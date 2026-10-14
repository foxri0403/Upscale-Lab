using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Projects;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ProjectProcessingServiceTests
{
    [Theory]
    [InlineData(ProcessingOperation.Layers)]
    [InlineData(ProcessingOperation.Upscale)]
    [InlineData(ProcessingOperation.UpscaleAndLayers)]
    public async Task Execute_PersistsOptionsAndPublishesOnlyRequestedResults(ProcessingOperation operation)
    {
        await using var fixture = await Fixture.Create();
        var status = await fixture.Service.StartAsync(fixture.Owner, fixture.Project.Id, default, new(operation, LayerEngine.PartGlee, 4));
        await fixture.Service.ExecuteAsync(status.JobId, default);
        var job = await fixture.Db.ProcessingJobs.SingleAsync();
        Assert.Equal(ProcessingJobStatus.Completed, job.Status);
        Assert.Equal(operation, fixture.Processor.Request!.Operation);
        Assert.Equal(LayerEngine.PartGlee, fixture.Processor.Request.LayerEngine);
        Assert.Equal(4, fixture.Processor.Request.UpscaleFactor);
        if (operation == ProcessingOperation.Upscale)
            Assert.Equal("old-layer", (await fixture.Db.ImageLayers.SingleAsync()).ObjectKey);
        else
            Assert.DoesNotContain("old-layer", fixture.Storage.Objects.Keys);
        if (operation != ProcessingOperation.Layers)
        {
            var download = await fixture.Service.GetDownloadAsync(fixture.Owner, fixture.Project.Id, job.Id, default);
            Assert.Equal(8, download.Width);
            Assert.Contains(job.UpscaledObjectKey!, download.Url);
        }
    }

    [Fact]
    public async Task Start_RejectsOtherOwnerInvalidOptionsAndDuplicateJob()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.StartAsync(Guid.NewGuid(), f.Project.Id, default));
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.StartAsync(f.Owner, f.Project.Id, default, new(UpscaleFactor: 3)));
        await f.Service.StartAsync(f.Owner, f.Project.Id, default);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.StartAsync(f.Owner, f.Project.Id, default));
        await Assert.ThrowsAsync<ConflictException>(() => new ProjectService(f.Db, f.Storage).DeleteAsync(f.Owner, f.Project.Id, default));
    }

    [Fact]
    public async Task Recover_RetainsProcessingOptionsAndQueuesInterruptedJob()
    {
        await using var f = await Fixture.Create();
        var status = await f.Service.StartAsync(f.Owner, f.Project.Id, default, new(ProcessingOperation.UpscaleAndLayers, LayerEngine.PartGlee, 4));
        var job = await f.Db.ProcessingJobs.SingleAsync();
        job.Status = ProcessingJobStatus.Processing;
        await f.Db.SaveChangesAsync();
        f.Queue.Ids.Clear();
        await f.Service.RecoverPendingAsync(default);
        Assert.Equal(new[] { status.JobId }, f.Queue.Ids);
        Assert.Equal(ProcessingJobStatus.Queued, job.Status);
        Assert.Equal(4, job.UpscaleFactor);
        Assert.Equal(LayerEngine.PartGlee, job.LayerEngine);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedUploadOrCancellation_PreservesOldLayersAndRemovesPartialArtifacts(bool cancel)
    {
        await using var f = await Fixture.Create();
        using var cancellation = new CancellationTokenSource();
        var status = await f.Service.StartAsync(f.Owner, f.Project.Id, default, new(ProcessingOperation.UpscaleAndLayers));
        f.Storage.OnUpload = key =>
        {
            if (!key.Contains("/layers/")) return;
            if (cancel) { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }
            throw new IOException("Internal storage trace");
        };
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.ExecuteAsync(status.JobId, cancellation.Token));
        else await f.Service.ExecuteAsync(status.JobId, default);
        var job = await f.Db.ProcessingJobs.SingleAsync();
        Assert.Equal(cancel ? ProcessingJobStatus.Queued : ProcessingJobStatus.Failed, job.Status);
        Assert.Null(job.UpscaledObjectKey);
        Assert.Equal("old-layer", (await f.Db.ImageLayers.SingleAsync()).ObjectKey);
        Assert.Equal(2, f.Storage.Objects.Count);
        Assert.DoesNotContain("Internal storage trace", job.ErrorMessage ?? "");
    }

    [Fact]
    public async Task DownloadsAreOwnerScopedAndDeletionCleansUpscaledArtifacts()
    {
        await using var f = await Fixture.Create();
        var status = await f.Service.StartAsync(f.Owner, f.Project.Id, default, new(ProcessingOperation.Upscale));
        await f.Service.ExecuteAsync(status.JobId, default);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetDownloadAsync(Guid.NewGuid(), f.Project.Id, status.JobId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetDownloadAsync(f.Owner, Guid.NewGuid(), status.JobId, default));
        await new ProjectService(f.Db, f.Storage).DeleteAsync(f.Owner, f.Project.Id, default);
        Assert.Empty(f.Storage.Objects);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Storage Storage { get; } = new();
        public Processor Processor { get; } = new();
        public Queue Queue { get; } = new();
        public Guid Owner { get; } = Guid.NewGuid();
        public LiveLayerProject Project { get; private set; } = null!;
        public ProjectProcessingService Service => new(Db, Storage, Processor, Queue, NullLogger<ProjectProcessingService>.Instance);
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            f.Project = new LiveLayerProject { UserId = f.Owner, Title = "Test", OriginalImage = new Image
                { UserId = f.Owner, FileName = "input.png", OriginalObjectKey = "original", ContentType = "image/png" } };
            f.Project.Layers.Add(new ImageLayer { ObjectKey = "old-layer", LayerOrder = 0 });
            f.Db.Add(f.Project);
            await f.Db.SaveChangesAsync();
            f.Storage.Objects["original"] = [1, 2];
            f.Storage.Objects["old-layer"] = [3, 4];
            return f;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Processor : IProjectImageProcessor
    {
        public ProjectProcessingRequest? Request { get; private set; }
        public async Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            var file = Path.Combine(request.OutputDirectory, "result.png");
            await File.WriteAllBytesAsync(file, [5, 6], cancellationToken);
            return new([new(file, LayerType.Background, 0, 0, 0, 0, 0, 1, 0, 0)], file, 8, 8);
        }
    }
    private sealed class Queue : IProcessingJobQueue
    {
        public List<Guid> Ids { get; } = [];
        public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken) { Ids.Add(jobId); return ValueTask.CompletedTask; }
        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    }
    private sealed class Storage : IStorageService
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        public Action<string>? OnUpload { get; set; }
        public async Task<StoredObject> UploadAsync(Stream content, string objectKey, string contentType, CancellationToken cancellationToken)
        {
            OnUpload?.Invoke(objectKey);
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            Objects[objectKey] = memory.ToArray();
            return new(objectKey, "s3://test/" + objectKey);
        }
        public Task DownloadAsync(string objectKey, Stream destination, CancellationToken cancellationToken) => destination.WriteAsync(Objects[objectKey], cancellationToken).AsTask();
        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) { Objects.Remove(objectKey); return Task.CompletedTask; }
        public string CreateDownloadUrl(string objectKey, TimeSpan lifetime) => "https://test/" + objectKey;
    }
}
