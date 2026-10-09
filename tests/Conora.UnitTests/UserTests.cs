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
}
