using Conora.Services;

namespace Conora.Worker.Jobs;

/// <summary>Daily purge of LGPD export files past the 30-day S3 retention.</summary>
public sealed class LgpdExportPurgeWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LgpdExportPurgeWorker> _logger;

    public LgpdExportPurgeWorker(IServiceScopeFactory scopes, ILogger<LgpdExportPurgeWorker> logger)
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
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var count = await scope.ServiceProvider.GetRequiredService<LgpdService>()
                    .PurgeExpiredExportFilesAsync(stoppingToken);
                if (count > 0)
                    _logger.LogInformation("Purgadas {Count} exportações LGPD expiradas no S3.", count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha no purge de exportações LGPD.");
            }
        }
    }
}
