namespace Conora.Services.Contracts;

public sealed record MemberRequest(string Name, bool IsActive = true);

public sealed record MemberResponse(Guid Id, string Name, bool IsActive);
