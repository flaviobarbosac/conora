using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record CategoryResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    string? Code,
    string? DisplayNumber,
    CategoryLevel Level,
    CategorySection Section,
    bool IsSystem,
    bool IsActive,
    int SortOrder,
    bool AcceptsPosting);

public sealed record CreateCategoryRequest(string Name, Guid ParentId);

public sealed record UpdateCategoryRequest(string Name, bool IsActive = true);
