using System.Text;
using Microsoft.Extensions.Configuration;

namespace Conora.Infrastructure.Security;

public sealed class JwtSettings
{
    public const string DefaultIssuer = "conora-api";
    public const string DefaultAudience = "conora-app";
    public const int MinKeyLength = 32;

    public required byte[] KeyBytes { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public TimeSpan AccessLifetime { get; init; }

    public static JwtSettings FromConfiguration(IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < MinKeyLength)
        {
            throw new InvalidOperationException(
                $"Jwt:Key must be configured with at least {MinKeyLength} characters (no hardcoded fallback).");
        }

        var accessMinutes = int.TryParse(configuration["Jwt:AccessMinutes"], out var minutes) ? minutes : 15;
        return new JwtSettings
        {
            KeyBytes = Encoding.UTF8.GetBytes(key),
            Issuer = string.IsNullOrWhiteSpace(configuration["Jwt:Issuer"])
                ? DefaultIssuer
                : configuration["Jwt:Issuer"]!,
            Audience = string.IsNullOrWhiteSpace(configuration["Jwt:Audience"])
                ? DefaultAudience
                : configuration["Jwt:Audience"]!,
            AccessLifetime = TimeSpan.FromMinutes(accessMinutes)
        };
    }
}
