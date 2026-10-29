using Conora.Domain.Ports;

namespace Conora.Api.Middleware;

public sealed class HttpCorrelationContext : ICorrelationContext
{
    private const string ActorHeaderName = "X-Actor";
    private const string ClientHeaderName = "X-Conora-Client";

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

    public string? ClientIp
    {
        get
        {
            var http = _accessor.HttpContext;
            if (http is null)
                return null;

            var forwarded = http.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first))
                    return first;
            }

            return http.Connection.RemoteIpAddress?.ToString();
        }
    }

    public bool FromNativeApp =>
        _accessor.HttpContext?.Request.Headers[ClientHeaderName].FirstOrDefault() is "ios" or "android";
}
