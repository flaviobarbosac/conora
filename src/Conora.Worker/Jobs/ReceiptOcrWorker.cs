using Conora.Services;

namespace Conora.Worker.Jobs;

public sealed class ReceiptOcrWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ReceiptOcrWorker> _logger;

    public ReceiptOcrWorker(IServiceScopeFactory scopes, ILogger<ReceiptOcrWorker> logger)
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
                var service = scope.ServiceProvider.GetRequiredService<ReceiptIntakeService>();
                var processed = await service.ProcessPendingBatchAsync(10, stoppingToken);
                if (processed == 0)
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha no worker de OCR de recibos.");
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }
}
