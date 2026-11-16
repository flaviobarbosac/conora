using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Conora.IntegrationTests;

/// <summary>
/// New account from registration through onboarding. Web and app both read the same profile.
/// </summary>
public sealed class NewAccountJornadaTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _client;

    public NewAccountJornadaTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task New_account_saves_diagnosis_and_web_and_app_load_the_same_journey()
    {
        var email = $"jornada-{Guid.NewGuid():N}@example.com";
        const string password = "senha-segura";
        const string cpf = "390.533.447-05";

        var registered = await _client.PostAsJsonAsync("/auth/register", new
        {
            Name = "Conta Nova",
            Email = email,
            Cpf = cpf,
            Password = password
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var web = await registered.Content.ReadFromJsonAsync<AuthDto>(JsonOptions);
        Assert.NotNull(web);

        var profileUpdate = await SendAsync(HttpMethod.Put, "/family/profile", web.AccessToken, new
        {
            Name = "Conta Nova",
            Phone = "27999999999",
            Cep = "29000-000"
        });
        Assert.Equal(HttpStatusCode.OK, profileUpdate.StatusCode);

        var account = await SendAsync(HttpMethod.Post, "/accounts", web.AccessToken, new
        {
            Name = "Conta corrente",
            Kind = 0
        });
        Assert.Equal(HttpStatusCode.Created, account.StatusCode);

        var saved = await SendAsync(HttpMethod.Put, "/family/profile/jornada", web.AccessToken, new
        {
            Display = "shown",
            Answers = new
            {
                RendaFixa = true,
                Divida = false,
                Patrimonio = true,
                ProjetoVida = false
            }
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var completed = await SendAsync(HttpMethod.Post, "/family/onboarding/complete", web.AccessToken, null);
        Assert.True(completed.StatusCode == HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());

        var onWeb = await ReadProfileAsync(web.AccessToken);

        var login = await _client.PostAsJsonAsync("/auth/login", new { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var app = await login.Content.ReadFromJsonAsync<AuthDto>(JsonOptions);
        Assert.NotNull(app);
        Assert.NotEqual(web.AccessToken, app.AccessToken);

        var onApp = await ReadProfileAsync(app.AccessToken);

        Assert.True(onWeb.OnboardingCompleted);
        Assert.Equal("shown", onWeb.HomeJourneyDisplay);
        Assert.Equal(onWeb.HomeJourneyDisplay, onApp.HomeJourneyDisplay);
        Assert.Equal(onWeb.OnboardingCompleted, onApp.OnboardingCompleted);
        Assert.NotNull(onWeb.JornadaDiagnostico);
        Assert.NotNull(onApp.JornadaDiagnostico);
        Assert.Equal(true, onWeb.JornadaDiagnostico.RendaFixa);
        Assert.Equal(false, onWeb.JornadaDiagnostico.Divida);
        Assert.Equal(true, onWeb.JornadaDiagnostico.Patrimonio);
        Assert.Equal(false, onWeb.JornadaDiagnostico.ProjetoVida);
        Assert.Equal(onWeb.JornadaDiagnostico, onApp.JornadaDiagnostico);
    }

    private async Task<ProfileDto> ReadProfileAsync(string accessToken)
    {
        var response = await SendAsync(HttpMethod.Get, "/family/profile", accessToken, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<ProfileDto>(JsonOptions);
        Assert.NotNull(profile);
        return profile;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string accessToken, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private sealed record AuthDto(Guid UserId, string Email, string AccessToken, string RefreshToken, DateTime AccessExpiresAtUtc);

    private sealed record ProfileDto(
        bool OnboardingCompleted,
        string HomeJourneyDisplay,
        JornadaDto? JornadaDiagnostico);

    private sealed record JornadaDto(bool? RendaFixa, bool? Divida, bool? Patrimonio, bool? ProjetoVida);
}
