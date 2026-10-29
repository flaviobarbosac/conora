using Conora.Domain.Enums;

namespace Conora.Services.CategoryAccess;

public sealed class FullCategoryAccessPolicy : ICategoryAccessPolicy
{
    public bool CanWriteAnalytical(string? categoryCode, CategorySection section) => true;
}
