using Conora.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace Conora.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("conora_test")
        .WithUsername("conora")
        .WithPassword("conora_test_password")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                ["Jwt:Key"] = "conora-test-jwt-key-must-be-32-chars!",
                ["Jwt:Issuer"] = "conora-api",
                ["Jwt:Audience"] = "conora-app",
                ["Cors:Origins:0"] = "http://localhost:5173",
                ["Email:SmtpHost"] = "",
                ["RegistrationAccess:Closed"] = "false"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICepLookup>();
            services.AddSingleton<ICepLookup, TestCepLookup>();
        });
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}

sealed class TestCepLookup : ICepLookup
{
    public Task<bool> ExistsAsync(string cep, CancellationToken ct)
    {
        var digits = new string((cep ?? "").Where(char.IsDigit).ToArray());
        return Task.FromResult(digits.Length == 8);
    }
}
