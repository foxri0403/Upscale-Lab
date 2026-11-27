using UpscaleLab.Infrastructure.Images;

namespace UpscaleLab.Api.Background;

public sealed class WebpImageWorker(IServiceScopeFactory scopes, WebpImageQueue queue, ILogger<WebpImageWorker> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(PrepareNewImagesAsync(stoppingToken), BackfillAsync(stoppingToken));

    private async Task PrepareNewImagesAsync(CancellationToken ct)
    {
        await foreach (var item in queue.ReadAllAsync(ct))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<WebpImageService>().PrepareAsync(item, ct);
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(error, "Stored WebP will be retried for {Kind} {ImageId}.", item.Kind, item.Id);
            }
        }
    }

    private async Task BackfillAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var (ready, failed) = await scope.ServiceProvider.GetRequiredService<WebpImageService>().BackfillAsync(ct);
                logger.LogInformation("WebP image backfill checked: {Ready} ready, {Failed} failed.", ready, failed);
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(error, "WebP image backfill will retry.");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
