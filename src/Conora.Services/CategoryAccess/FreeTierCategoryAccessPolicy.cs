using Conora.Domain.Catalog;
using Conora.Domain.Enums;

namespace Conora.Services.CategoryAccess;

/// <summary>Free plan: only free-tier analytical codes are writable.</summary>
public sealed class FreeTierCategoryAccessPolicy : ICategoryAccessPolicy
{
    public bool CanWriteAnalytical(string? categoryCode, CategorySection section)
    {
        if (string.IsNullOrWhiteSpace(categoryCode))
            return false;
        return FreeTierLimits.AllowsCategoryCode(categoryCode);
    }
}
