using Conora.Domain.Catalog;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class ChartAccount : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Code { get; private set; }
    public string? DisplayNumber { get; private set; }
    public ChartAccountLevel Level { get; private set; }
    public ChartSection Section { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    public bool AcceptsPosting => Level == ChartAccountLevel.Analytical && IsActive;

    private ChartAccount()
    {
    }

    public static ChartAccount CreateAnalytical(string name, Guid parentId, ChartSection section, int sortOrder = 0)
    {
        return new ChartAccount
        {
            Name = RequireName(name),
            ParentId = parentId,
            Level = ChartAccountLevel.Analytical,
            Section = section,
            IsSystem = false,
            SortOrder = sortOrder
        };
    }

    public static ChartAccount CreateSystem(SystemChartAccountDefinition definition, Guid? parentId) => new()
    {
        Name = definition.Name,
        Code = definition.Code,
        ParentId = parentId,
        Level = definition.Level,
        Section = definition.Section,
        IsSystem = true,
        SortOrder = definition.SortOrder
    };

    public void SetDisplayNumber(string? displayNumber) => DisplayNumber = displayNumber;

    public void Rename(string name)
    {
        EnsureNotSystem();
        if (Level != ChartAccountLevel.Analytical)
            throw new ValidationException("name", "Só contas analíticas podem ser renomeadas.");

        Name = RequireName(name);
    }

    public void SetActive(bool active)
    {
        EnsureNotSystem();
        if (Level != ChartAccountLevel.Analytical)
            throw new ValidationException("isActive", "Só contas analíticas podem ser ativadas ou desativadas.");

        IsActive = active;
    }

    public void SyncFromDefinition(SystemChartAccountDefinition definition, Guid? parentId)
    {
        if (!IsSystem || Code != definition.Code)
            return;

        Name = definition.Name;
        ParentId = parentId;
        Level = definition.Level;
        Section = definition.Section;
        SortOrder = definition.SortOrder;
    }

    public void EnsureNotSystem()
    {
        if (IsSystem)
            throw new SystemChartAccountProtectedException();
    }

    public void EnsureAnalytical()
    {
        if (Level != ChartAccountLevel.Analytical)
            throw new ValidationException("chartAccountId", "Só contas analíticas recebem valor.");
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("name", "Nome é obrigatório.");

        return name.Trim();
    }
}
