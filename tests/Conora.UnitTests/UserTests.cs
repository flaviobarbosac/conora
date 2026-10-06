using Conora.Domain.Entities;
using Xunit;

namespace Conora.UnitTests;

public class UserTests
{
    [Fact]
    public void Create_normalizes_email_to_lowercase()
    {
        var user = User.Create("Ana", "Ana@Example.com");

        Assert.Equal("ana@example.com", user.Email);
    }

    [Theory]
    [InlineData("", "a@b.com")]
    [InlineData("Nome", "")]
    public void Create_throws_when_required_field_is_missing(string name, string email)
    {
        Assert.Throws<ArgumentException>(() => User.Create(name, email));
    }
}
