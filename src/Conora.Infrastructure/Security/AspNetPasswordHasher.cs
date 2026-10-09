using Conora.Domain.Ports;
using Microsoft.AspNetCore.Identity;

namespace Conora.Infrastructure.Security;

public sealed class AspNetPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(new object(), password);

    public bool Verify(string password, string passwordHash)
        => _hasher.VerifyHashedPassword(new object(), passwordHash, password) is not PasswordVerificationResult.Failed;
}
