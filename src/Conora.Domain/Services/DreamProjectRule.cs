using Conora.Domain.Entities;

namespace Conora.Domain.Services;

/// <summary>
/// Milestone 30 ("Primeiro aporte no projeto de vida") counts only dream projects. Money kept in the reserve
/// categories (emergency reserve, annual-expenses reserve) is savings, not a dream, so it never grants the milestone.
/// System and user-created life-project categories both count.
/// </summary>
public static class DreamProjectRule
{
    public static readonly IReadOnlyList<string> ReserveCategoryCodes = ["LIFE_RESERVE", "LIFE_ANNUAL"];

    /// <summary>First project with money in it whose category is not a reserve one; null when there is none.</summary>
    public static LifeProject? FirstFundedDream(IEnumerable<LifeProject> projects, IReadOnlySet<Guid> reserveCategoryIds)
        => projects.FirstOrDefault(p =>
            p.AccumulatedAmount > 0 && !(p.CategoryId is { } categoryId && reserveCategoryIds.Contains(categoryId)));
}
