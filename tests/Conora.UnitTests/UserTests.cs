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
    public void MarkEmailVerified_clears_verification_token()
    {
        var user = User.Create("Ana", "ana@example.com");
        user.IssueEmailVerification("vhash", DateTime.UtcNow.AddHours(24));
        Assert.True(user.EmailVerificationIsActive("vhash"));

        user.MarkEmailVerified();

        Assert.True(user.EmailVerified);
        Assert.False(user.EmailVerificationIsActive("vhash"));
    }
}
