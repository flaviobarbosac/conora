using Conora.Services;
using Xunit;

namespace Conora.UnitTests;

public class WhatsAppPairingCodeTests
{
    [Fact]
    public void Code_is_single_use_and_the_message_links_the_user()
    {
        var userId = Guid.NewGuid();
        var code = WhatsAppPairingCodes.Issue(userId);

        Assert.Matches("^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$", code);
        Assert.Equal(userId, WhatsAppPairingCodes.TryConsume($"CONORA {code}"));
        Assert.Null(WhatsAppPairingCodes.TryConsume($"CONORA {code}"));
        Assert.Null(WhatsAppPairingCodes.TryConsume("oi"));
    }

    [Fact]
    public void A_new_code_for_the_same_user_invalidates_the_previous_one()
    {
        var userId = Guid.NewGuid();
        var first = WhatsAppPairingCodes.Issue(userId);
        var second = WhatsAppPairingCodes.Issue(userId);

        Assert.Null(WhatsAppPairingCodes.TryConsume($"conora {first}"));
        Assert.Equal(userId, WhatsAppPairingCodes.TryConsume($"Olá CONORA {second}"));
    }

    [Fact]
    public void An_expired_code_does_not_link_and_cannot_be_used_again()
    {
        var userId = Guid.NewGuid();
        var code = WhatsAppPairingCodes.Issue(userId);
        var afterTtl = DateTime.UtcNow.Add(WhatsAppPairingCodes.Ttl).AddSeconds(1);

        Assert.Null(WhatsAppPairingCodes.TryConsume($"CONORA {code}", afterTtl));
        Assert.Null(WhatsAppPairingCodes.TryConsume($"CONORA {code}"));
    }
}
