namespace Conora.Domain.Services;

public sealed record IanSpeechVoice(string VoiceName, string Style);

/// <summary>
/// Picks the Gemini voice and speaking style for Ian or Clara at a maturity band.
/// The voice follows the portrait: child, preteen, teen and adult. A missing or unknown band is adult.
/// </summary>
public static class IanVoiceProfile
{
    public const string BandChild = "child";
    public const string BandPreteen = "preteen";
    public const string BandTeen = "teen";
    public const string BandAdult = "adult";

    private const string AdultStyle = "voz humana, calma e natural, português do Brasil";

    public static IanSpeechVoice Resolve(string? voice, string? band)
    {
        var clara = string.Equals(voice, "clara", StringComparison.OrdinalIgnoreCase);
        return (band?.Trim().ToLowerInvariant(), clara) switch
        {
            (BandChild, false) => new("Puck", "voz de menino de 5 anos, criança pequena, alegre e doce, português do Brasil"),
            (BandChild, true) => new("Leda", "voz de menina de 5 anos, criança pequena, alegre e doce, português do Brasil"),
            (BandPreteen, false) => new("Puck", "voz de menino de 11 anos, pré-adolescente, animado e natural, português do Brasil"),
            (BandPreteen, true) => new("Leda", "voz de menina de 11 anos, pré-adolescente, animada e natural, português do Brasil"),
            (BandTeen, false) => new("Fenrir", "voz de rapaz de 16 anos, adolescente, descontraído e natural, português do Brasil"),
            (BandTeen, true) => new("Zephyr", "voz de moça de 16 anos, adolescente, descontraída e natural, português do Brasil"),
            (_, true) => new("Aoede", AdultStyle),
            _ => new("Charon", AdultStyle)
        };
    }
}
