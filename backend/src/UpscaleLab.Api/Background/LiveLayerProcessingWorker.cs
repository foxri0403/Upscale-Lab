using UpscaleLab.Application.Processing;

namespace UpscaleLab.Api.Background;

public sealed class LiveLayerProcessingWorker(
    IProcessingJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<LiveLayerProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var jobId = await queue.DequeueAsync(stoppingToken);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IProjectProcessingService>();
                await service.ExecuteAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unexpected failure while executing processing job {JobId}", jobId);
            }
        }
    }
}
