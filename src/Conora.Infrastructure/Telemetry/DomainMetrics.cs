using System.Diagnostics.Metrics;
using Conora.Domain.Ports;

namespace Conora.Infrastructure.Telemetry;

public sealed class DomainMetrics : IDomainMetrics, IDisposable
{
    public const string MeterName = "Conora.Domain";

    private readonly Meter _meter;
    private readonly Counter<long> _usersCreated;

    public DomainMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _usersCreated = _meter.CreateCounter<long>(
            "users_created_total", description: "Total de usuários criados.");
    }

    public void RecordUserCreated() => _usersCreated.Add(1);

    public void Dispose() => _meter.Dispose();
}
