using System.Net;
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
    public async Task Create_user_returns_created()
    {
        var response = await _client.PostAsJsonAsync("/users", new { Name = "Ana", Email = "ana@example.com" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(user);
        Assert.Equal("ana@example.com", user.Email);
    }

    [Fact]
    public async Task Create_duplicate_email_returns_conflict()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        var first = await _client.PostAsJsonAsync("/users", new { Name = "A", Email = email });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/users", new { Name = "B", Email = email });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    private sealed record UserDto(Guid Id, string Name, string Email, DateTime CreatedAtUtc);
}
