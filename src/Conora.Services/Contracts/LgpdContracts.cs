namespace Conora.Services.Contracts;

public sealed record LgpdExportResponse(Guid UserId, string Email, string Name, DateTime ExportedAtUtc, object Data);
