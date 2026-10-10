using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Conora.IntegrationTests;

public sealed class TenantIsolationTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _client;

    public TenantIsolationTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task TenantA_list_does_not_include_TenantB_account()
    {
        var a = await RegisterAsync();
        var b = await RegisterAsync();

        var accountB = await CreateAccountAsync(b, "Conta B");
        var listA = await ListAccountsAsync(a);
        Assert.DoesNotContain(listA, x => x.Id == accountB.Id);

        var listB = await ListAccountsAsync(b);
        Assert.Contains(listB, x => x.Id == accountB.Id);
    }

    [Fact]
    public async Task TenantA_cannot_delete_TenantB_account()
    {
        var a = await RegisterAsync();
        var b = await RegisterAsync();

        var accountB = await CreateAccountAsync(b, "Conta B");
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/accounts/{accountB.Id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", a.AccessToken);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var listB = await ListAccountsAsync(b);
        Assert.Contains(listB, x => x.Id == accountB.Id);
    }

    [Fact]
    public async Task TenantA_cannot_read_TenantB_entry_by_id()
    {
        var a = await RegisterAsync();
        var b = await RegisterAsync();

        var accountB = await CreateAccountAsync(b, "Caixa B");
        var entryB = await CreateIncomeAsync(b, accountB.Id, 100m);
        var response = await AuthedGetAsync(a, $"/entries/{entryB.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TenantA_get_user_B_returns_not_found()
    {
        var a = await RegisterAsync();
        var b = await RegisterAsync();

        var response = await AuthedGetAsync(a, $"/users/{b.UserId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TenantA_dashboard_does_not_include_TenantB_income()
    {
        var a = await RegisterAsync();
        var b = await RegisterAsync();

        var ym = DateTime.UtcNow.ToString("yyyy-MM");
        var accountA = await CreateAccountAsync(a, "Caixa A");
        var accountB = await CreateAccountAsync(b, "Caixa B");
        await CreateIncomeAsync(a, accountA.Id, 50m);
        await CreateIncomeAsync(b, accountB.Id, 9_999m);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/dashboard/{ym}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", a.AccessToken);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("9999", body, StringComparison.Ordinal);
        Assert.DoesNotContain("9.999", body, StringComparison.Ordinal);
    }

    private async Task<AuthDto> RegisterAsync()
    {
        var email = $"iso-{Guid.NewGuid():N}@example.com";
        var register = await _client.PostAsJsonAsync("/auth/register", new
        {
            Name = "Iso",
            Email = email,
            Cpf = NewValidCpf(),
            Password = "senha-segura"
        });
        Assert.True(
            register.StatusCode == HttpStatusCode.Created,
            await register.Content.ReadAsStringAsync());
        var auth = await register.Content.ReadFromJsonAsync<AuthDto>(JsonOptions);
        Assert.NotNull(auth);
        return auth;
    }

    private static string NewValidCpf()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        var digits = new int[9];
        for (var i = 0; i < 9; i++)
            digits[i] = bytes[i] % 10;

        // Avoid obvious invalid all-equal sequences.
        if (digits.Distinct().Count() == 1)
            digits[8] = (digits[8] + 1) % 10;

        var d1 = CpfCheckDigit(digits, 10);
        var d2 = CpfCheckDigit(digits.Append(d1).ToArray(), 11);
        return $"{digits[0]}{digits[1]}{digits[2]}.{digits[3]}{digits[4]}{digits[5]}.{digits[6]}{digits[7]}{digits[8]}-{d1}{d2}";
    }

    private static int CpfCheckDigit(IReadOnlyList<int> digits, int weightStart)
    {
        var sum = 0;
        for (var i = 0; i < digits.Count; i++)
            sum += digits[i] * (weightStart - i);
        var mod = sum % 11;
        return mod < 2 ? 0 : 11 - mod;
    }

    private async Task<AccountDto> CreateAccountAsync(AuthDto auth, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/accounts")
        {
            Content = JsonContent.Create(new { Name = name, Kind = 1, OpeningBalance = 0 })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var response = await _client.SendAsync(request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var account = await response.Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        Assert.NotNull(account);
        return account;
    }

    private async Task<List<AccountDto>> ListAccountsAsync(AuthDto auth)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/accounts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<AccountDto>>(JsonOptions);
        Assert.NotNull(list);
        return list;
    }

    private async Task<EntryDto> CreateIncomeAsync(AuthDto auth, Guid accountId, decimal amount)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/entries")
        {
            Content = JsonContent.Create(new
            {
                Type = 1,
                Amount = amount,
                OccurredAt = DateTime.UtcNow,
                Description = "Iso income",
                AccountId = accountId
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var response = await _client.SendAsync(request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<List<EntryDto>>(JsonOptions);
        Assert.NotNull(created);
        Assert.NotEmpty(created);
        return created[0];
    }

    private async Task<HttpResponseMessage> AuthedGetAsync(AuthDto auth, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return await _client.SendAsync(request);
    }

    private sealed record AuthDto(Guid UserId, string Email, string AccessToken, string RefreshToken, DateTime AccessExpiresAtUtc);
    private sealed record AccountDto(Guid Id, string Name);
    private sealed record EntryDto(Guid Id, decimal Amount);
}
