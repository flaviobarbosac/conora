using Conora.Services;

namespace Conora.Worker.Jobs;

/// <summary>Notices accounts whose tasting ends within 24 hours. Runs on the same cadence as the other mail workers.</summary>
public sealed class TrialEndingNoticeWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<TrialEndingNoticeWorker> _logger;

    public TrialEndingNoticeWorker(IServiceScopeFactory scopes, ILogger<TrialEndingNoticeWorker> logger)
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
                var sent = await scope.ServiceProvider.GetRequiredService<TrialEndingNoticeService>()
                    .SendDueAsync(stoppingToken);
                if (sent > 0)
                    _logger.LogInformation("Avisos de fim de degustação enviados: {Count}.", sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha no aviso de fim de degustação.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
