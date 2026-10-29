using Conora.Services;

namespace Conora.Worker.Jobs;

public sealed class PreRegistrationPurgeWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PreRegistrationPurgeWorker> _logger;

    public PreRegistrationPurgeWorker(IServiceScopeFactory scopes, ILogger<PreRegistrationPurgeWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var purged = await scope.ServiceProvider.GetRequiredService<AuthService>()
                    .PurgeExpiredPreRegistrationsAsync(stoppingToken);
                if (purged > 0)
                    _logger.LogInformation("Expurgados {Count} pré-cadastros expirados.", purged);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha no expurgo de pré-cadastros.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
