using Conora.Domain.Ports;
using Conora.Infrastructure.Persistence;
using Conora.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HealthCheckResult = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult;

namespace Conora.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = RequireConnection(sp, "Postgres");
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName!);
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            });
        });

        services.AddStackExchangeRedisCache(options =>
        {
            options.InstanceName = "conora:";
        });

        services.AddOptions<RedisCacheOptions>()
            .PostConfigure<IConfiguration>((options, config) =>
            {
                options.Configuration = RequireConnection(config, "Redis");
            });

        services.AddSingleton<IDomainMetrics, DomainMetrics>();

        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
            .AddNpgSql(sp => RequireConnection(sp, "Postgres"), name: "postgres", tags: new[] { "ready" })
            .AddRedis(sp => RequireConnection(sp, "Redis"), name: "redis", tags: new[] { "ready" });

        return services;
    }

    private static string RequireConnection(IServiceProvider sp, string name)
        => RequireConnection(sp.GetRequiredService<IConfiguration>(), name);

    private static string RequireConnection(IConfiguration configuration, string name)
    {
        var value = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"ConnectionStrings:{name} não configurada.");

        return value;
    }
}
