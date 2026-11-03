using Conora.Services;
using Xunit;

namespace Conora.UnitTests;

public class RegistrationAccessTests
{
    private static RegistrationAccessOptions Closed(params string[] emails) => new()
    {
        Closed = true,
        AllowedEmails = emails
    };

    [Fact]
    public void Open_registration_allows_anyone()
    {
        var access = new RegistrationAccessOptions { Closed = false };

        Assert.True(access.Allows("fora@example.com"));
    }

    [Fact]
    public void Closed_registration_allows_listed_email_ignoring_case()
    {
        var access = Closed("Flavio@elroy.com.br");

        Assert.True(access.Allows(" flavio@elroy.com.br "));
        Assert.False(access.Allows("outra@example.com"));
        Assert.False(access.Allows(null));
    }

    [Fact]
    public void Closed_message_is_the_public_notice()
    {
        Assert.Equal("Acesso em breve.", RegistrationAccessOptions.ClosedMessage);
    }
}
