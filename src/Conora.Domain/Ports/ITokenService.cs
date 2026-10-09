namespace Conora.Domain.Ports;

public sealed record TokenPair(string AccessToken, string RefreshToken, DateTime AccessExpiresAtUtc);

public interface ITokenService
{
    TokenPair Issue(Guid userId, string email);
    Guid? ReadUserId(string accessToken);
    string HashRefreshToken(string refreshToken);
}
