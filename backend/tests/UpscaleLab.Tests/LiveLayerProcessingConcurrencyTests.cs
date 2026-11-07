using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UpscaleLab.Api.Background;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Projects;
using UpscaleLab.Infrastructure.Processing;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class LiveLayerProcessingConcurrencyTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Consumers_ProcessDifferentJobsConcurrently_WithoutExceedingConfiguredLimit(int concurrency)
    {
        const int totalJobs = 6;
        var queue = new ProcessingJobQueue();
        var service = new BlockingProcessingService(concurrency, totalJobs);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Processing:MaxConcurrentJobs"] = concurrency.ToString()
        }).Build();
        using var provider = new ServiceCollection()
            .AddSingleton<IProjectProcessingService>(service)
            .BuildServiceProvider();
        using var worker = new LiveLayerProcessingWorker(
            queue, provider.GetRequiredService<IServiceScopeFactory>(), config,
            NullLogger<LiveLayerProcessingWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            for (var i = 0; i < totalJobs; i++)
                await queue.EnqueueAsync(Guid.NewGuid(), CancellationToken.None);

            await service.AllSlotsOccupied.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(concurrency, service.ActiveJobs);
            Assert.Equal(concurrency, service.StartedJobs);
            Assert.Equal(concurrency, service.MaximumActiveJobs);

            service.Release();
            await service.AllJobsFinished.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(totalJobs, service.StartedJobs);
            Assert.Equal(totalJobs, service.CompletedJobs);
            Assert.True(service.MaximumActiveJobs <= concurrency);
            Assert.Equal(0, service.ActiveJobs);
        }
        finally
        {
            service.Release();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class BlockingProcessingService(int slots, int totalJobs) : IProjectProcessingService
    {
        private readonly TaskCompletionSource<bool> allSlots = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> allJobs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int active;
        private int started;
        private int completed;
        private int peak;

        public int ActiveJobs => Volatile.Read(ref active);
        public int StartedJobs => Volatile.Read(ref started);
        public int CompletedJobs => Volatile.Read(ref completed);
        public int MaximumActiveJobs => Volatile.Read(ref peak);
        public Task AllSlotsOccupied => allSlots.Task;
        public Task AllJobsFinished => allJobs.Task;
        public void Release() => released.TrySetResult(true);

        public Task RecoverPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref active);
            var observed = Volatile.Read(ref peak);
            while (now > observed)
            {
                var previous = Interlocked.CompareExchange(ref peak, now, observed);
                if (previous == observed) break;
                observed = previous;
            }
            if (Interlocked.Increment(ref started) == slots)
                allSlots.TrySetResult(true);
            try
            {
                await released.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref active);
                if (Interlocked.Increment(ref completed) == totalJobs)
                    allJobs.TrySetResult(true);
            }
        }

        public Task<ProcessingStatusResponse> RefineSubjectAsync(Guid userId, Guid projectId, Guid baseLayerId, Stream mask, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<ProcessingStatusResponse> RefineSelectionAsync(Guid userId, Guid projectId, Guid baseLayerId, Stream selection, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<ProcessingStatusResponse> CancelAsync(Guid userId, Guid projectId, Guid jobId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<ProcessingStatusResponse> StartAsync(Guid userId, Guid projectId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<ProcessingStatusResponse> StartAsync(Guid userId, Guid projectId, ProcessProjectRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<DownloadUrlResponse> GetResultUrlAsync(Guid userId, Guid projectId, Guid jobId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<ProcessingStatusResponse> GetStatusAsync(Guid userId, Guid projectId, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
