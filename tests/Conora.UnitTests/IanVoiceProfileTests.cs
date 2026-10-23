using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class IanVoiceProfileTests
{
    [Theory]
    [InlineData("ian", "child", "Puck")]
    [InlineData("ian", "preteen", "Puck")]
    [InlineData("ian", "teen", "Fenrir")]
    [InlineData("ian", "adult", "Charon")]
    [InlineData("clara", "child", "Leda")]
    [InlineData("clara", "preteen", "Leda")]
    [InlineData("clara", "teen", "Zephyr")]
    [InlineData("clara", "adult", "Aoede")]
    public void Resolve_picks_the_voice_for_each_band(string voice, string band, string expected)
    {
        Assert.Equal(expected, IanVoiceProfile.Resolve(voice, band).VoiceName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("toddler")]
    public void Resolve_falls_back_to_the_adult_voice(string? band)
    {
        Assert.Equal("Charon", IanVoiceProfile.Resolve("ian", band).VoiceName);
        Assert.Equal("Aoede", IanVoiceProfile.Resolve("Clara", band).VoiceName);
    }

    [Fact]
    public void Resolve_ignores_band_case_and_spaces()
    {
        Assert.Equal("Leda", IanVoiceProfile.Resolve("clara", " Child ").VoiceName);
    }

    [Fact]
    public void Child_styles_ask_for_a_child_voice()
    {
        Assert.Contains("5 anos", IanVoiceProfile.Resolve("ian", "child").Style);
        Assert.Contains("5 anos", IanVoiceProfile.Resolve("clara", "child").Style);
    }
}
