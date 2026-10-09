using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Conora.IntegrationTests;

public class UserTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public UserTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Post_users_without_auth_is_not_found_or_method_not_allowed()
    {
        var response = await _client.PostAsJsonAsync("/users", new { Name = "Ana", Email = "ana@example.com" });
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.Unauthorized,
            $"Unexpected status: {response.StatusCode}");
    }

    [Fact]
    public async Task Get_user_without_auth_returns_unauthorized()
    {
        var register = await RegisterAsync($"iso-{Guid.NewGuid():N}@example.com", "390.533.447-05");
        var response = await _client.GetAsync($"/users/{register.UserId}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_own_user_with_auth_returns_ok()
    {
        var auth = await RegisterAsync($"me-{Guid.NewGuid():N}@example.com", "153.509.460-56");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/users/{auth.UserId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_duplicate_email_returns_conflict()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        var first = await _client.PostAsJsonAsync("/auth/register", new
        {
            Name = "A",
            Email = email,
            Cpf = "529.982.247-25",
            Password = "senha-segura"
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/auth/register", new
        {
            Name = "B",
            Email = email,
            Cpf = "111.444.777-35",
            Password = "senha-segura"
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Register_and_login_returns_tokens()
    {
        var email = $"auth-{Guid.NewGuid():N}@example.com";
        const string cpf = "862.883.667-57";
        var register = await _client.PostAsJsonAsync("/auth/register", new
        {
            Name = "Ana",
            Email = email,
            Cpf = cpf,
            Password = "senha-segura"
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var loginEmail = await _client.PostAsJsonAsync("/auth/login", new { Email = email, Password = "senha-segura" });
        Assert.Equal(HttpStatusCode.OK, loginEmail.StatusCode);

        var loginCpf = await _client.PostAsJsonAsync("/auth/login", new { Usuario = cpf, Password = "senha-segura" });
        Assert.Equal(HttpStatusCode.OK, loginCpf.StatusCode);
        var body = await loginCpf.Content.ReadFromJsonAsync<AuthDto>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
    }

    [Fact]
    public async Task Audit_events_without_auth_returns_unauthorized()
    {
        var response = await _client.GetAsync("/audit-events");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<AuthDto> RegisterAsync(string email, string cpf)
    {
        var register = await _client.PostAsJsonAsync("/auth/register", new
        {
            Name = "Ana",
            Email = email,
            Cpf = cpf,
            Password = "senha-segura"
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var auth = await register.Content.ReadFromJsonAsync<AuthDto>();
        Assert.NotNull(auth);
        return auth;
    }

    private sealed record AuthDto(Guid UserId, string Email, string AccessToken, string RefreshToken, DateTime AccessExpiresAtUtc);
}
