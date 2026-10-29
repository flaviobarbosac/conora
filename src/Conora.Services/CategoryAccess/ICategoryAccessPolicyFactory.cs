namespace Conora.Services.CategoryAccess;

/// <summary>Resolves the category write-access strategy for the current plan.</summary>
public interface ICategoryAccessPolicyFactory
{
    ICategoryAccessPolicy ForFullAccess(bool hasFullAccess);
}
