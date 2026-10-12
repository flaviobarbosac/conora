using Conora.Services;

namespace Conora.Worker.Jobs;

public sealed class ReceiptImagePurgeWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ReceiptImagePurgeWorker> _logger;

    public ReceiptImagePurgeWorker(IServiceScopeFactory scopes, ILogger<ReceiptImagePurgeWorker> logger)
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
                using var scope = _scopes.CreateScope();
                var purged = await scope.ServiceProvider.GetRequiredService<ReceiptIntakeService>()
                    .PurgeExpiredImagesAsync(stoppingToken);
                if (purged > 0)
                    _logger.LogInformation("Expurgadas {Count} imagens de recibo expiradas.", purged);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha no expurgo de imagens de recibo.");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
