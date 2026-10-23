using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Conora.Domain.Ports;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Conora.IntegrationTests;

/// <summary>End-to-end store billing: app confirmation, cross-login protection, store notices and webhook auth.</summary>
public sealed class StoreBillingTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly FakeStoreValidator _store = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public StoreBillingTests(ApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IStoreReceiptValidator>(_store)));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Confirmed_apple_purchase_activates_the_plan_and_store_notice_ends_it()
    {
        var user = await RegisterAsync();
        var original = $"orig-{Guid.NewGuid():N}";
        _store.Receipt = new StoreReceipt(
            "conora.duo.yearly", "tx-1", original, DateTime.UtcNow.AddYears(1), Entitled: true, InTrial: false, user.UserId);

        var confirm = await PostAsync(user, "/plan/store/confirm", new { channel = 2, productId = "conora.duo.yearly", transactionId = "tx-1" });
        Assert.True(confirm.IsSuccessStatusCode, await confirm.Content.ReadAsStringAsync());
        var plan = await confirm.Content.ReadFromJsonAsync<PlanDto>(JsonOptions);
        Assert.Equal("Subscribed", plan!.Phase);
        Assert.Equal("Family", plan.Tier);
        Assert.True(plan.Yearly);

        // App Store REFUND notice for the same original transaction, without an account token.
        using (var scope = _factory.Services.CreateScope())
        {
            var billing = scope.ServiceProvider.GetRequiredService<IStoreSubscriptionBilling>();
            await billing.ApplyNoticeAsync(new StoreNoticeCommand(
                StoreBillingChannel.Apple,
                "REFUND",
                new StoreReceipt("conora.duo.yearly", "tx-1", original, DateTime.UtcNow, Entitled: false, InTrial: false, AccountId: null)));
        }

        var after = await GetPlanAsync(user);
        Assert.Equal("Expired", after.Phase);
    }

    [Fact]
    public async Task Store_purchase_is_bound_to_the_first_login()
    {
        var owner = await RegisterAsync();
        var other = await RegisterAsync();
        var token = $"gp-{Guid.NewGuid():N}";
        _store.Receipt = new StoreReceipt(
            "conora.individual.monthly", "GPA.1", token, DateTime.UtcNow.AddMonths(1), Entitled: true, InTrial: false, AccountId: null);

        var first = await PostAsync(owner, "/plan/store/confirm",
            new { channel = 1, productId = "conora.individual.monthly", transactionId = "GPA.1", purchaseToken = token });
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

        var second = await PostAsync(other, "/plan/store/confirm",
            new { channel = 1, productId = "conora.individual.monthly", transactionId = "GPA.1", purchaseToken = token });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.NotEqual("Subscribed", (await GetPlanAsync(other)).Phase);
    }

    [Fact]
    public async Task Unpaid_or_unknown_store_purchase_is_refused()
    {
        var user = await RegisterAsync();
        _store.Receipt = new StoreReceipt(
            "conora.individual.monthly", "tx-p", $"orig-{Guid.NewGuid():N}", null, Entitled: false, InTrial: false, user.UserId);
        var pending = await PostAsync(user, "/plan/store/confirm",
            new { channel = 2, productId = "conora.individual.monthly", transactionId = "tx-p" });
        Assert.Equal(HttpStatusCode.BadRequest, pending.StatusCode);

        _store.Receipt = new StoreReceipt(
            "conora.gold.monthly", "tx-u", $"orig-{Guid.NewGuid():N}", DateTime.UtcNow.AddMonths(1), true, false, user.UserId);
        var unknown = await PostAsync(user, "/plan/store/confirm",
            new { channel = 2, productId = "conora.gold.monthly", transactionId = "tx-u" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.NotEqual("Subscribed", (await GetPlanAsync(user)).Phase);
    }

    [Fact]
    public async Task Webhooks_reject_unauthenticated_payloads()
    {
        var google = await _client.PostAsJsonAsync("/webhooks/store/google-play",
            new { message = new { data = "e30=" } });
        Assert.Equal(HttpStatusCode.Unauthorized, google.StatusCode);

        var apple = await _client.PostAsJsonAsync("/webhooks/store/app-store",
            new { signedPayload = "eyJhbGciOiJub25lIn0.eyJ4IjoxfQ.c2ln" });
        Assert.Equal(HttpStatusCode.BadRequest, apple.StatusCode);
    }

    private async Task<HttpResponseMessage> PostAsync(AuthDto auth, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return await _client.SendAsync(request);
    }

    private async Task<PlanDto> GetPlanAsync(AuthDto auth)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/plan");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlanDto>(JsonOptions))!;
    }

    private async Task<AuthDto> RegisterAsync()
    {
        var register = await _client.PostAsJsonAsync("/auth/register", new
        {
            Name = "Loja",
            Email = $"store-{Guid.NewGuid():N}@example.com",
            Cpf = NewValidCpf(),
            Password = "senha-segura"
        });
        Assert.True(register.StatusCode == HttpStatusCode.Created, await register.Content.ReadAsStringAsync());
        return (await register.Content.ReadFromJsonAsync<AuthDto>(JsonOptions))!;
    }

    private static string NewValidCpf()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        var digits = new int[9];
        for (var i = 0; i < 9; i++)
            digits[i] = bytes[i] % 10;
        if (digits.Distinct().Count() == 1)
            digits[8] = (digits[8] + 1) % 10;

        var d1 = CpfCheckDigit(digits, 10);
        var d2 = CpfCheckDigit(digits.Append(d1).ToArray(), 11);
        return $"{digits[0]}{digits[1]}{digits[2]}.{digits[3]}{digits[4]}{digits[5]}.{digits[6]}{digits[7]}{digits[8]}-{d1}{d2}";
    }

    private static int CpfCheckDigit(int[] digits, int weightStart)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
            sum += digits[i] * (weightStart - i);
        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }

    private sealed class FakeStoreValidator : IStoreReceiptValidator
    {
        public StoreReceipt? Receipt { get; set; }

        public Task<StoreReceipt> ValidateAsync(
            StoreBillingChannel channel, string productId, string transactionId, string purchaseToken, CancellationToken ct = default)
            => Task.FromResult(Receipt ?? throw new InvalidOperationException("A loja não confirmou esta compra."));

        public Task<StoreReceipt> FetchGoogleSubscriptionAsync(string purchaseToken, CancellationToken ct = default)
            => Task.FromResult(Receipt ?? throw new InvalidOperationException("A Google Play não confirmou esta compra."));
    }

    private sealed record AuthDto(Guid UserId, string Email, string AccessToken);

    private sealed record PlanDto(string Phase, string? Tier, bool Yearly);
}
