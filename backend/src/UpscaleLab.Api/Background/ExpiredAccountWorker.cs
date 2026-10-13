using UpscaleLab.Application.Auth;

namespace UpscaleLab.Api.Background;

public sealed class ExpiredAccountWorker(IServiceScopeFactory scopes, ILogger<ExpiredAccountWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IAuthService>().DeleteExpiredAccountsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "Account/session cleanup failed; retrying in 30 seconds."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
