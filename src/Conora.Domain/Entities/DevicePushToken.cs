using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

/// <summary>FCM registration token for one app install, owned by the tenant user.</summary>
public class DevicePushToken : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string Token { get; private set; } = default!;
    public string Platform { get; private set; } = default!;
    public DateTime LastSeenAt { get; private set; }

    private DevicePushToken()
    {
    }

    public static DevicePushToken Register(string token, string platform)
    {
        var normalized = NormalizeToken(token);
        var plat = NormalizePlatform(platform);
        return new DevicePushToken
        {
            Token = normalized,
            Platform = plat,
            LastSeenAt = DateTime.UtcNow
        };
    }

    public void Touch(string platform)
    {
        Platform = NormalizePlatform(platform);
        LastSeenAt = DateTime.UtcNow;
    }

    public static string NormalizeToken(string token)
    {
        var value = (token ?? string.Empty).Trim();
        if (value.Length is < 20 or > 4096)
            throw new ValidationException("token", "Token de push inválido.");
        return value;
    }

    public static string NormalizePlatform(string platform)
    {
        var value = (platform ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "ios" or "android" or "web" => value,
            _ => throw new ValidationException("platform", "Informe a plataforma: ios, android ou web.")
        };
    }
}
