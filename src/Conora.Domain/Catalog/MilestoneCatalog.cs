namespace Conora.Domain.Catalog;

/// <summary>Ian maturity ages and points (total 1.200). Baby (0) does not score.</summary>
public static class MilestoneCatalog
{
    public static readonly IReadOnlyList<MilestoneDefinition> Ordered =
    [
        new(8, "budget-half-filled", "Primeiro orçamento", 80),
        new(10, "first-month-closed", "Primeiro mês fechado", 120),
        new(12, "three-months-in-budget", "Três meses seguidos", 200),
        new(15, "payment-on-time", "Um mês no prazo", 100),
        new(18, "reserve-started", "Reserva iniciada", 150),
        new(22, "reserve-complete", "Reserva completa", 250),
        new(30, "project-progress", "Aporte em projeto de vida", 300)
    ];

    public static MilestoneDefinition? ByKey(string key)
        => Ordered.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.Ordinal));

    public static MilestoneDefinition? ByAge(int age)
        => Ordered.FirstOrDefault(m => m.Age == age);

    /// <summary>Highest age that may be shown: only consecutive grants from 8 upward (no skip).</summary>
    public static int DisplayAge(IEnumerable<int> grantedAges)
    {
        var set = grantedAges.ToHashSet();
        var age = 0;
        foreach (var def in Ordered)
        {
            if (!set.Contains(def.Age))
                break;
            age = def.Age;
        }
        return age;
    }
}

public sealed record MilestoneDefinition(int Age, string Key, string Title, int Points);
