using Conora.Domain.Enums;

namespace Conora.Services.CategoryAccess;

/// <summary>Strategy for plan-based category write access (free vs full).</summary>
public interface ICategoryAccessPolicy
{
    bool CanWriteAnalytical(string? categoryCode, CategorySection section);
}
