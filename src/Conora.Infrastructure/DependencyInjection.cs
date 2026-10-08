using Conora.Domain.Ports;
using Conora.Infrastructure.Email;
using Conora.Infrastructure.Messaging;
using Conora.Infrastructure.Persistence;
using Conora.Infrastructure.Security;
using Conora.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HealthCheckResult = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult;

namespace Conora.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITenantContext, TenantContext>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = RequireConnection(configuration, "Postgres");
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
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
        services.AddScoped<IEmailSuppressionStore, EfEmailSuppressionStore>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddHttpClient("ses-sns", client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient<IGeminiClient, Ai.GeminiClient>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient<IWhatsAppMessenger, WhatsApp.CloudApiWhatsAppMessenger>(client =>
        {
            client.BaseAddress = new Uri("https://graph.facebook.com/v21.0/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddMessaging(configuration);

        var health = services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
            .AddNpgSql(_ => RequireConnection(configuration, "Postgres"), name: "postgres", tags: new[] { "ready" })
            .AddRedis(_ => RequireConnection(configuration, "Redis"), name: "redis", tags: new[] { "ready" });

        return services;
    }

    private static string RequireConnection(IConfiguration configuration, string name)
    {
        var value = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"ConnectionStrings:{name} não configurada.");

        return value;
    }
}
