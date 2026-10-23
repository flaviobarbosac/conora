using Conora.Services;

namespace Conora.Worker.Jobs;

/// <summary>Once per calendar day (America/Sao_Paulo), evaluates Ian milestones for every active user.</summary>
public sealed class MilestoneNightlyWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MilestoneNightlyWorker> _logger;

    public MilestoneNightlyWorker(IServiceScopeFactory scopes, ILogger<MilestoneNightlyWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextBrazilNight();
            _logger.LogInformation("Next milestone job in {Delay}.", delay);
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
                await scope.ServiceProvider.GetRequiredService<MilestoneEvaluationService>()
                    .RunAllUsersAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha no job noturno de marcos.");
            }
        }
    }

    /// <summary>Next 02:00 in Brazil (or soon if we just missed it).</summary>
    private static TimeSpan DelayUntilNextBrazilNight()
    {
        var tzId = OperatingSystem.IsWindows()
            ? "E. South America Standard Time"
            : "America/Sao_Paulo";
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        var nowBrazil = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var next = nowBrazil.Date.AddHours(2);
        if (nowBrazil >= next)
            next = next.AddDays(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(next, DateTimeKind.Unspecified), tz);
        var delay = nextUtc - DateTime.UtcNow;
        return delay < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : delay;
    }
}
