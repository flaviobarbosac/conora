namespace Conora.Services.Contracts;

public sealed record RegisterPushTokenRequest(string Token, string Platform);

public sealed record UnregisterPushTokenRequest(string Token);

public sealed record PushTokenResponse(Guid Id, string Token, string Platform, DateTime LastSeenAt);

public sealed record SendPushRequest(
    string Title,
    string Body,
    IReadOnlyDictionary<string, string>? Data = null,
    IReadOnlyList<Guid>? UserIds = null);

public sealed record SendPushResponse(
    bool Configured,
    int Attempted,
    int Succeeded,
    int Failed,
    int TokensRemoved,
    string? Detail = null);
