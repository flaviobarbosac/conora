using Conora.Services;

namespace Conora.Worker.Jobs;

/// <summary>Once per night (~03:00 America/Sao_Paulo), processes pending LGPD export requests.</summary>
public sealed class LgpdExportNightlyWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LgpdExportNightlyWorker> _logger;

    public LgpdExportNightlyWorker(IServiceScopeFactory scopes, ILogger<LgpdExportNightlyWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextBrazilNight();
            _logger.LogInformation("Next LGPD export job in {Delay}.", delay);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var count = await scope.ServiceProvider.GetRequiredService<LgpdService>()
                    .ProcessPendingExportsAsync(stoppingToken);
                if (count > 0)
                    _logger.LogInformation("Processadas {Count} exportações LGPD pendentes.", count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha no job noturno de exportação LGPD.");
            }
        }
    }

    /// <summary>Next 03:00 in Brazil — low traffic window for heavy DB reads.</summary>
    private static TimeSpan DelayUntilNextBrazilNight()
    {
        var tzId = OperatingSystem.IsWindows()
            ? "E. South America Standard Time"
            : "America/Sao_Paulo";
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        var nowBrazil = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var next = nowBrazil.Date.AddHours(3);
        if (nowBrazil >= next)
            next = next.AddDays(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(next, DateTimeKind.Unspecified), tz);
        var delay = nextUtc - DateTime.UtcNow;
        return delay < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : delay;
    }
}
