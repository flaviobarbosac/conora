using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Conora.Services;

/// <summary>One active pairing code per user. The first inbound message burns it.</summary>
public static class WhatsAppPairingCodes
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly Regex CodePattern = new(@"CONORA\s+([A-Z2-9]{8})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly ConcurrentDictionary<string, Pairing> ByCode = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<Guid, string> ByUser = new();

    public static string Issue(Guid userId)
    {
        if (ByUser.TryRemove(userId, out var previous))
            ByCode.TryRemove(previous, out _);

        var code = NewCode();
        ByCode[code] = new Pairing(userId, DateTime.UtcNow.Add(Ttl));
        ByUser[userId] = code;
        return code;
    }

    public static Guid? TryConsume(string text, DateTime? utcNow = null)
    {
        var match = CodePattern.Match(text);
        if (!match.Success)
            return null;

        var code = match.Groups[1].Value;
        if (!ByCode.TryRemove(code, out var pairing))
            return null;

        ByUser.TryRemove(pairing.UserId, out _);
        var now = utcNow ?? DateTime.UtcNow;
        return pairing.ExpiresAtUtc > now ? pairing.UserId : null;
    }

    private static string NewCode()
    {
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    private sealed record Pairing(Guid UserId, DateTime ExpiresAtUtc);
}
