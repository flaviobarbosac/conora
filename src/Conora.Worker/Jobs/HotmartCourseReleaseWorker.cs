using Conora.Services;

namespace Conora.Worker.Jobs;

/// <summary>Releases Hotmart student access and sends the store e-mail once 98 days have passed.</summary>
public sealed class HotmartCourseReleaseWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<HotmartCourseReleaseWorker> _logger;

    public HotmartCourseReleaseWorker(IServiceScopeFactory scopes, ILogger<HotmartCourseReleaseWorker> logger)
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
                var released = await scope.ServiceProvider.GetRequiredService<HotmartCourseService>()
                    .ReleaseDueAsync(stoppingToken);
                if (released > 0)
                    _logger.LogInformation("Liberados {Count} acessos de aluno Hotmart.", released);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha na liberação do acesso Hotmart.");
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
