using Microsoft.Extensions.DependencyInjection;

namespace Conora.Services;

public static class DependencyInjection
{
    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<UserService>();
        services.AddScoped<AuditService>();
        return services;
    }
}
