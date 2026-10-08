using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record ChartAccountResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    string? Code,
    string? DisplayNumber,
    ChartAccountLevel Level,
    ChartSection Section,
    bool IsSystem,
    bool IsActive,
    int SortOrder,
    bool AcceptsPosting);

public sealed record CreateChartAccountRequest(string Name, Guid ParentId);

public sealed record UpdateChartAccountRequest(string Name, bool IsActive = true);
