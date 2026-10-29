using Conora.Domain.Catalog;

namespace Conora.Services.CategoryAccess;

/// <summary>Factory selecting category access strategy by plan entitlements.</summary>
public sealed class CategoryAccessPolicyFactory : ICategoryAccessPolicyFactory
{
    public ICategoryAccessPolicy ForFullAccess(bool hasFullAccess) =>
        hasFullAccess ? new FullCategoryAccessPolicy() : new FreeTierCategoryAccessPolicy();
}
