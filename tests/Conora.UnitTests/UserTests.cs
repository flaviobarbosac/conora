using Conora.Domain.Entities;
using Xunit;

namespace Conora.UnitTests;

public class UserTests
{
    [Fact]
    public void Create_normalizes_email_to_lowercase()
    {
        var user = User.Create("Ana", "Ana@Example.com", "529.982.247-25");

        Assert.Equal("ana@example.com", user.Email);
        Assert.Equal("52998224725", user.Cpf);
    }

    [Theory]
    [InlineData("", "a@b.com")]
    [InlineData("Nome", "")]
    public void Create_throws_when_required_field_is_missing(string name, string email)
    {
        Assert.Throws<ArgumentException>(() => User.Create(name, email));
    }

    [Fact]
    public void ApplyPassword_sets_local_password_and_keeps_google()
    {
        var user = User.Create("Ana", "ana@example.com");
        user.SetGoogleId("google-1");
        user.IssuePasswordReset("hash", DateTime.UtcNow.AddMinutes(30));

        Assert.True(user.PasswordResetIsActive("hash"));

        user.ApplyPassword("hashed-password");

        Assert.Equal("hashed-password", user.PasswordHash);
        Assert.Equal("google-1", user.GoogleId);
        Assert.False(user.EmailVerified);
        Assert.False(user.PasswordResetIsActive("hash"));
    }

    [Fact]
    public void Password_reset_cooldown_outlasts_the_token_for_operators()
    {
        var user = User.Create("Ana", "ana@example.com");
        var ttl = TimeSpan.FromMinutes(30);
        var issuedAt = DateTime.UtcNow;
        user.IssuePasswordReset("hash", issuedAt.Add(ttl));
        var twoHoursLater = issuedAt.AddHours(2);

        Assert.False(user.IsPasswordResetCoolingDown(ttl, TimeSpan.FromMinutes(15), twoHoursLater));
        Assert.True(user.IsPasswordResetCoolingDown(ttl, TimeSpan.FromHours(24), twoHoursLater));
    }

    [Fact]
    public void Password_reset_cooldown_is_open_when_no_token_was_issued()
    {
        var user = User.Create("Ana", "ana@example.com");
        var now = DateTime.UtcNow;

        Assert.False(user.IsPasswordResetCoolingDown(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(15), now));
    }

    [Fact]
    public void MarkEmailVerified_keeps_hash_for_idempotent_link_replay()
    {
        var user = User.Create("Ana", "ana@example.com");
        user.IssueEmailVerification("vhash", DateTime.UtcNow.AddHours(24));
        Assert.True(user.EmailVerificationIsActive("vhash"));

        user.MarkEmailVerified();

        Assert.True(user.EmailVerified);
        Assert.True(user.EmailVerificationIsActive("vhash"));
    }
}
