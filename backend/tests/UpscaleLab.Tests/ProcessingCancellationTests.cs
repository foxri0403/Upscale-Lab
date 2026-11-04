using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Upscaling;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ProcessingCancellationTests
{
    [Fact]
    public async Task QueuedCancellation_ConsumesOnceAndNeverRunsOrRecovers()
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid()); db.Add(project); await db.SaveChangesAsync();
        var tokens = new FakeTokenService(); var processor = new ControlledProcessor(); var queue = new ProcessingJobQueue();
        var service = Service(db, new WorkflowStore(), processor, tokens, queue);
        var job = await service.StartAsync(project.UserId, project.Id, new(ProcessingOperation.Upscale, 2), default);
        await Assert.ThrowsAsync<NotFoundException>(() => service.CancelAsync(Guid.NewGuid(), project.Id, job.JobId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.CancelAsync(project.UserId, Guid.NewGuid(), job.JobId, default));
        Assert.Equal(0, tokens.CompletedCount);
        Assert.Equal(ProcessingJobStatus.Cancelled, (await service.CancelAsync(project.UserId, project.Id, job.JobId, default)).Status);
        await service.CancelAsync(project.UserId, project.Id, job.JobId, default);
        await service.ExecuteAsync(job.JobId, default);
        await service.RecoverPendingAsync(default);
        Assert.Equal(1, tokens.CompletedCount); Assert.Equal(0, tokens.RefundedCount);
        Assert.False(processor.Entered.Task.IsCompleted);
        Assert.Equal(ProjectStatus.Uploaded, project.Status);
        Assert.NotNull((await db.ProcessingJobs.SingleAsync()).CompletedAt);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RunningCancellation_DiscardsLateSuccessOrFailureWithoutRefund(bool ignoreCancellation, bool fail)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var workerDb = new ApplicationDbContext(options);
        await using var apiDb = new ApplicationDbContext(options);
        var project = WorkflowData.Project(Guid.NewGuid());
        project.Layers.Add(new ImageLayer { ObjectKey = "existing.png", LayerType = LayerType.Background });
        workerDb.Add(project); await workerDb.SaveChangesAsync();
        var store = new WorkflowStore(); using var photo = new MagickImage(MagickColors.Blue, 10, 10);
        store.Files["source.png"] = photo.ToByteArray(MagickFormat.Png); store.Files["existing.png"] = [1];
        var tokens = new FakeTokenService(); var processor = new ControlledProcessor(ignoreCancellation, fail);
        var worker = Service(workerDb, store, processor, tokens);
        var api = Service(apiDb, store, processor, tokens);
        var started = await worker.StartAsync(project.UserId, project.Id, new(ProcessingOperation.Upscale, 2), default);
        var running = worker.ExecuteAsync(started.JobId, default);
        await processor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await api.CancelAsync(project.UserId, project.Id, started.JobId, default);
        processor.Release.TrySetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(10));
        workerDb.ChangeTracker.Clear();
        var result = await workerDb.ProcessingJobs.SingleAsync();
        Assert.Equal(ProcessingJobStatus.Cancelled, result.Status);
        Assert.Null(result.ResultObjectKey);
        Assert.Equal("existing.png", (await workerDb.ImageLayers.SingleAsync()).ObjectKey);
        Assert.Equal(2, store.Files.Count);
        Assert.Equal(1, tokens.CompletedCount); Assert.Equal(0, tokens.RefundedCount);
        await worker.ExecuteAsync(started.JobId, default);
        Assert.Equal(1, tokens.CompletedCount);
    }

    [Theory]
    [InlineData(ProcessingJobStatus.Completed)]
    [InlineData(ProcessingJobStatus.Failed)]
    public async Task TerminalJobs_CannotBeCancelledOrChargedAgain(ProcessingJobStatus status)
    {
        await using var db = WorkflowData.Db(); var project = WorkflowData.Project(Guid.NewGuid());
        var job = new ProcessingJob { Project = project, Status = status }; db.Add(job); await db.SaveChangesAsync();
        var tokens = new FakeTokenService(); var service = Service(db, new WorkflowStore(), new ControlledProcessor(), tokens);
        await Assert.ThrowsAsync<ConflictException>(() => service.CancelAsync(project.UserId, project.Id, job.Id, default));
        Assert.Equal(0, tokens.CompletedCount); Assert.Equal(0, tokens.RefundedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UserCancellation_AlsoCancelsRemoteUpscaleWithIndependentToken(bool s3)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options); await using var apiDb = new ApplicationDbContext(options);
        var project = WorkflowData.Project(Guid.NewGuid()); db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); using var image = new MagickImage(MagickColors.Blue, 10, 10);
        store.Files["source.png"] = image.ToByteArray(MagickFormat.Png);
        using var cloud = new RemoteCloud(); using var client = new HttpClient(cloud);
        var processor = new RemoteProcessor(client, s3); var tokens = new FakeTokenService();
        var service = Service(db, store, processor, tokens); var api = Service(apiDb, store, processor, tokens);
        var job = await service.StartAsync(project.UserId, project.Id, new(ProcessingOperation.Upscale, TargetLongEdge: 1920), default);
        var execution = service.ExecuteAsync(job.JobId, default);
        await cloud.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await api.CancelAsync(project.UserId, project.Id, job.JobId, default);
        await execution.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, cloud.Cancels); Assert.Equal(0, tokens.RefundedCount); Assert.Equal(1, tokens.CompletedCount);
    }

    private static ProjectProcessingService Service(ApplicationDbContext db, WorkflowStore store, IProjectImageProcessor processor,
        FakeTokenService tokens, ProcessingJobQueue? queue = null) => new(db, store, processor, queue ?? new(),
        NullLogger<ProjectProcessingService>.Instance, tokens);

    private sealed class RemoteProcessor(HttpClient client, bool s3) : IProjectImageProcessor
    {
        public bool Supports(ProcessingOperation operation) => true;
        public async Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken ct)
        {
            var options = new RunPodOptions { ApiKey = "test-key", EndpointId = "test-endpoint", ApiBaseUrl = "https://provider.invalid/v2", TimeoutSeconds = 60 };
            if (s3) await new RunPodS3UpscaleService(client, options).UpscaleAsync("https://input.invalid", "https://output.invalid", 2, ct, 1920, request.JobId);
            else await new RunPodUpscaleService(client, options).UpscaleAsync(request.InputPath, 2, Path.Combine(request.OutputDirectory, "result.png"), ct, 1920, request.JobId);
            throw new InvalidOperationException("Cancelled remote work must not return a result.");
        }
    }

    private sealed class RemoteCloud : HttpMessageHandler
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Cancels;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);
            if (request.RequestUri!.AbsolutePath.EndsWith("/run"))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = "remote-job" }) };
            if (request.RequestUri.AbsolutePath.EndsWith("/cancel/remote-job"))
            {
                Assert.False(ct.IsCancellationRequested); Cancels++;
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = "CANCELLED" }) };
            }
            Waiting.TrySetResult(); await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("Polling must stop on cancellation.");
        }
    }

    private sealed class ControlledProcessor(bool ignoreCancellation = false, bool fail = false) : IProjectImageProcessor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Supports(ProcessingOperation operation) => true;
        public async Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ignoreCancellation ? CancellationToken.None : ct);
            if (fail) throw new InvalidOperationException("Late provider failure");
            var path = Path.Combine(request.OutputDirectory, "result.png");
            using var image = new MagickImage(MagickColors.Blue, 20, 20); image.Write(path, MagickFormat.Png);
            return new([], path, 20, 20);
        }
    }
}
