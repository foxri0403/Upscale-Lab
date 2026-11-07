using UpscaleLab.Application.Processing;

namespace UpscaleLab.Api.Background;

public sealed class LiveLayerProcessingWorker(
    IProcessingJobQueue queue,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<LiveLayerProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Recovery happens once, before any consumers start taking queued work.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IProjectProcessingService>();
                await service.RecoverPendingAsync(stoppingToken);
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Unable to recover LiveLayer jobs; retrying after the database becomes available");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        // This controls the EC2 API worker concurrency, not RunPod endpoint scaling.
        // Keep a bounded number of independent consumers, each with its own DI scope/DbContext.
        var configured = configuration.GetValue("Processing:MaxConcurrentJobs", 4);
        var concurrency = Math.Clamp(configured, 1, 4);
        if (configured != concurrency)
            logger.LogWarning("Processing:MaxConcurrentJobs={Configured} is outside 1-4; using {Concurrency}", configured, concurrency);
        logger.LogInformation("Starting {Concurrency} concurrent LiveLayer processing consumers", concurrency);

        var consumers = Enumerable.Range(1, concurrency)
            .Select(index => RunConsumerAsync(index, stoppingToken))
            .ToArray();
        await Task.WhenAll(consumers);
    }

    private async Task RunConsumerAsync(int consumerIndex, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid jobId;
            try
            {
                jobId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IProjectProcessingService>();
                await service.ExecuteAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Unexpected failure while executing processing job {JobId} in consumer {ConsumerIndex}",
                    jobId, consumerIndex);
            }
        }
    }
}
