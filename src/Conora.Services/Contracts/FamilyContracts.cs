namespace Conora.Services.Contracts;

public sealed record FamilyMemberUserResponse(Guid UsuarioId, string Name, string Email, bool IsSelf);

public sealed record FamilyInviteRequest(string Email);

public sealed record FamilyInviteResponse(
    Guid Id,
    string Email,
    DateTime ExpiresAt,
    DateTime? AcceptedAt,
    DateTime? CancelledAt,
    bool IsOpen);

public sealed record FamilyInvitePreviewResponse(
    string Status,
    string InviterName,
    string Email,
    DateTime? ExpiresAt);

public sealed record FamilyNoticeResponse(Guid Id, string Kind, string Message, DateTime CreatedAt, bool IsRead);

public sealed record FamilyGroupResponse(
    Guid? GroupId,
    IReadOnlyList<FamilyMemberUserResponse> Members,
    IReadOnlyList<FamilyInviteResponse> PendingInvites,
    IReadOnlyList<FamilyNoticeResponse> Notices);

public sealed record UpdateProfileRequest(string Name);
public sealed record ProfileResponse(Guid UsuarioId, string Name, string Email);
