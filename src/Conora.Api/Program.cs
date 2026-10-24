using System.Threading.RateLimiting;
using Conora.Api.Endpoints;
using Conora.Api.Exceptions;
using Conora.Api.Middleware;
using Conora.Domain.Ports;
using Conora.Infrastructure;
using Conora.Infrastructure.Persistence;
using Conora.Infrastructure.Security;
using Conora.Infrastructure.Telemetry;
using Conora.Repository;
using Conora.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    var postgres = builder.Configuration.GetConnectionString("Postgres") ?? "";
    var host = postgres.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
        .Where(pair => pair.Length == 2 && pair[0].Equals("Host", StringComparison.OrdinalIgnoreCase))
        .Select(pair => pair[1])
        .FirstOrDefault();
    if (host is not ("localhost" or "127.0.0.1"))
    {
        throw new InvalidOperationException(
            "Development só usa o Postgres local (localhost:5433). O banco da AWS é produção.");
    }
}

builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddRepositories();
builder.Services.AddServices();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, configuration) =>
    {
        // Read JWT from the final configuration so test hosts (WebApplicationFactory) override appsettings.
        var jwt = Conora.Infrastructure.Security.JwtSettings.FromConfiguration(configuration);
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwt.KeyBytes),
            NameClaimType = "sub",
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddOpenApi();

const string serviceName = "Conora.Api";
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(Conora.Services.Common.DomainActivitySource.Name)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        else
            tracing.AddConsoleExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(DomainMetrics.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        else
            metrics.AddConsoleExporter();
    });

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<TenantMiddleware>();
app.UseAuthorization();
app.UseMiddleware<PlanAccessMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapAuditEndpoints();
app.MapConsoleEndpoints();
app.MapLgpdEndpoints();
app.MapCategoryEndpoints();
app.MapDiagnosisEndpoints();
app.MapAccountEndpoints();
app.MapCreditCardEndpoints();
app.MapEntryEndpoints();
app.MapImportEndpoints();
app.MapBudgetEndpoints();
app.MapMonthEndpoints();
app.MapLifeProjectEndpoints();
app.MapPatrimonyEndpoints();
app.MapDashboardEndpoints();
app.MapMemberEndpoints();
app.MapFamilyEndpoints();
app.MapGamificationEndpoints();
app.MapPlanEndpoints();
app.MapFeedbackEndpoints();
app.MapHelpEndpoints();
app.MapAiEndpoints();
app.MapWhatsAppEndpoints();
app.MapReceiptEndpoints();
app.MapSesWebhookEndpoints();
app.MapStoreBillingWebhookEndpoints();
app.MapHotmartWebhookEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

await SyncSystemCategoriesAsync(app);

if (string.Equals(Environment.GetEnvironmentVariable("CONORA_SYNC_CATEGORIES_ONLY"), "1", StringComparison.Ordinal))
    return;

app.Run();

public partial class Program
{
    private static async Task SyncSystemCategoriesAsync(WebApplication app)
    {
        List<Guid> userIds;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var accounts = await db.Users.IgnoreQueryFilters()
                .Where(u => u.DeletedAt == null)
                .Select(u => u.Id)
                .ToListAsync();
            var owners = await db.Categories.IgnoreQueryFilters()
                .Where(c => c.DeletedAt == null)
                .Select(c => c.UsuarioId)
                .Distinct()
                .ToListAsync();
            userIds = accounts.Union(owners).Distinct().ToList();
        }

        foreach (var userId in userIds)
        {
            using var scope = app.Services.CreateScope();
            var tenant = (TenantContext)scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.Set(userId);
            await scope.ServiceProvider.GetRequiredService<CategoryService>().EnsureDefaultsAsync(CancellationToken.None);
        }
    }
}
