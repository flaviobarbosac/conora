using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Xunit;

namespace Conora.UnitTests;

public class DevicePushTokenTests
{
    [Fact]
    public void Register_normalizes_platform_and_keeps_token()
    {
        var row = DevicePushToken.Register("abcdefghijklmnopqrstuvwxyz", "Android");
        Assert.Equal("abcdefghijklmnopqrstuvwxyz", row.Token);
        Assert.Equal("android", row.Platform);
        Assert.True(row.LastSeenAt <= DateTime.UtcNow.AddSeconds(1));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("")]
    public void NormalizeToken_rejects_invalid(string token)
    {
        Assert.Throws<ValidationException>(() => DevicePushToken.NormalizeToken(token));
    }

    [Theory]
    [InlineData("desktop")]
    [InlineData("")]
    public void NormalizePlatform_rejects_unknown(string platform)
    {
        Assert.Throws<ValidationException>(() => DevicePushToken.NormalizePlatform(platform));
    }

    [Fact]
    public void Touch_updates_platform_and_last_seen()
    {
        var row = DevicePushToken.Register("abcdefghijklmnopqrstuvwxyz", "ios");
        var before = row.LastSeenAt;
        row.Touch("web");
        Assert.Equal("web", row.Platform);
        Assert.True(row.LastSeenAt >= before);
    }
}
