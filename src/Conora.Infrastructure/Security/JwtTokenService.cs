using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Conora.Domain.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Conora.Infrastructure.Security;

public sealed class JwtTokenService : ITokenService
{
    private readonly byte[] _key;
    private readonly TimeSpan _accessLifetime;

    public JwtTokenService(IConfiguration configuration)
    {
        _key = Encoding.UTF8.GetBytes(configuration["Jwt:Key"] ?? "conora-dev-jwt-key-must-be-32-chars!");
        _accessLifetime = TimeSpan.FromMinutes(int.TryParse(configuration["Jwt:AccessMinutes"], out var minutes) ? minutes : 15);
    }

    public TokenPair Issue(Guid userId, string email)
    {
        var expires = DateTime.UtcNow.Add(_accessLifetime);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email)
            ]),
            Expires = expires,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_key), SecurityAlgorithms.HmacSha256)
        });

        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return new TokenPair(handler.WriteToken(token), refresh, expires);
    }

    public Guid? ReadUserId(string accessToken)
    {
        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(accessToken, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(_key),
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);

            var raw = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
        catch
        {
            return null;
        }
    }

    public string HashRefreshToken(string refreshToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(hash);
    }
}
