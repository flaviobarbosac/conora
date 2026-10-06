using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record CategoryResponse(Guid Id, string Name, string? Code, CategoryKind Kind, bool IsSystem, bool IsActive, bool IsEssential);

public sealed record CreateCategoryRequest(string Name, CategoryKind Kind, bool IsEssential = false);

public sealed record UpdateCategoryRequest(string Name, bool IsEssential = false, bool IsActive = true);
