using Conora.Repository.Interface;
using Microsoft.Extensions.DependencyInjection;

namespace Conora.Repository;

public static class DependencyInjection
{
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuditEventRepository, AuditEventRepository>();
        return services;
    }
}
