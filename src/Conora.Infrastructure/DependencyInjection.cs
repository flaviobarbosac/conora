using Conora.Domain.Ports;
using Conora.Infrastructure.Billing;
using Conora.Infrastructure.Email;
using Conora.Infrastructure.Persistence;
using Conora.Infrastructure.Security;
using Conora.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
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

        services.AddSingleton<IDomainMetrics, DomainMetrics>();
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
        services.AddHttpClient(nameof(Security.AppleTokenValidator), client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<IAppleTokenValidator, Security.AppleTokenValidator>();
        services.AddHttpClient(nameof(Security.AppleTokenRevoker), client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<IAppleTokenRevoker, Security.AppleTokenRevoker>();
        services.AddScoped<IEmailSuppressionStore, EfEmailSuppressionStore>();
        services.Configure<HotmartOptions>(configuration.GetSection(HotmartOptions.SectionName));
        services.AddScoped<IHotmartCourseStore, EfHotmartCourseStore>();
        services.AddScoped<ISupplierStore, Persistence.EfSupplierStore>();
        services.AddScoped<IReceiptOcrQueue, Persistence.EfReceiptOcrQueue>();
        services.AddSingleton<IReceiptImageStore, Storage.LocalReceiptImageStore>();
        var lgpdBucket = configuration["LgpdExport:S3:Bucket"];
        if (!string.IsNullOrWhiteSpace(lgpdBucket))
            services.AddSingleton<ILgpdExportStore, Storage.S3LgpdExportStore>();
        else
            services.AddSingleton<ILgpdExportStore, Storage.LocalLgpdExportStore>();
        services.AddScoped<ILgpdExportDataSource, Storage.LgpdExportDataSource>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.Configure<StoreBillingOptions>(configuration.GetSection(StoreBillingOptions.SectionName));
        services.AddHttpClient<IStoreReceiptValidator, StoreReceiptValidator>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<IStoreNotificationReader, StoreNotificationReader>();
        services.AddScoped<IStoreBillingGateway, LocalStoreBillingGateway>();
        services.AddHttpClient("ses-sns", client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient<IGeminiClient, Ai.GeminiClient>(client => client.Timeout = TimeSpan.FromSeconds(45));
        services.AddHttpClient<IWhatsAppMessenger, WhatsApp.CloudApiWhatsAppMessenger>(client =>
        {
            client.BaseAddress = new Uri("https://graph.facebook.com/v21.0/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        var health = services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
            .AddNpgSql(_ => RequireConnection(configuration, "Postgres"), name: "postgres", tags: new[] { "ready" });

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
