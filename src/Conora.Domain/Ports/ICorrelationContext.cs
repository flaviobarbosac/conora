namespace Conora.Domain.Ports;

public interface ICorrelationContext
{
    string CorrelationId { get; }
    string Actor { get; }
}
