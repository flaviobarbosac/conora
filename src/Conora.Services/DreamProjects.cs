using Conora.Domain.Entities;
using Conora.Domain.Services;
using Conora.Repository.Interface;

namespace Conora.Services;

/// <summary>Loads the data <see cref="DreamProjectRule"/> needs; shared by the nightly evaluation and the status screen.</summary>
internal static class DreamProjects
{
    public static async Task<LifeProject?> FirstFundedDreamAsync(this IFinanceRepository repo, CancellationToken ct)
    {
        var codes = DreamProjectRule.ReserveCategoryCodes.ToArray();
        var reserveIds = (await repo.ListAsync<Category>(c => c.Code != null && codes.Contains(c.Code), ct))
            .Select(c => c.Id)
            .ToHashSet();
        var projects = await repo.ListAsync<LifeProject>(null, ct);
        return DreamProjectRule.FirstFundedDream(projects, reserveIds);
    }
}
