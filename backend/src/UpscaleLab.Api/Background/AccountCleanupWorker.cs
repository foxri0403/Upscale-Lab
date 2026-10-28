using UpscaleLab.Application.Auth;

namespace UpscaleLab.Api.Background;

public sealed class AccountCleanupWorker(IServiceScopeFactory scopes, ILogger<AccountCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IAccountCleanupService>().ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "Account cleanup will retry."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
