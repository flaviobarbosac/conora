using Conora.Domain.Ports;

namespace Conora.Api.Middleware;

public sealed class HttpCorrelationContext : ICorrelationContext
{
    private const string ActorHeaderName = "X-Actor";

    private readonly IHttpContextAccessor _accessor;

    public HttpCorrelationContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string CorrelationId =>
        _accessor.HttpContext?.Items[CorrelationIdMiddleware.ItemKey] as string
        ?? Guid.NewGuid().ToString("N");

    public string Actor =>
        _accessor.HttpContext?.Request.Headers[ActorHeaderName].FirstOrDefault()
        ?? "system";
}
