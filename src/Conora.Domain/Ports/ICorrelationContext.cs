namespace Conora.Domain.Ports;

public interface ICorrelationContext
{
    string CorrelationId { get; }
    string Actor { get; }

    /// <summary>Client IP (honors X-Forwarded-For when present).</summary>
    string? ClientIp { get; }

    /// <summary>True when the request came from the Conora iOS/Android app (not the website).</summary>
    bool FromNativeApp { get; }
}
