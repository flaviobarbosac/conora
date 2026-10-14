using Conora.Domain.Catalog;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class Category : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Code { get; private set; }
    public string? DisplayNumber { get; private set; }
    public CategoryLevel Level { get; private set; }
    public CategorySection Section { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }
    /// <summary>Kept for schema compatibility. Chart is shared; Personal is unused for categories.</summary>
    public SharingScope SharingScope { get; private set; } = SharingScope.Family;

    /// <summary>User who brought this analytical category into the group chart. Null on system accounts.</summary>
    public Guid? CreatedByUsuarioId { get; private set; }

    /// <summary>Same value on every member's copy. Null on system accounts.</summary>
    public Guid? FamilyKey { get; private set; }

    /// <summary>True until the budget owner accepts. Pending rows stay out of reports and posting.</summary>
    public bool IsPendingReview { get; private set; }

    public bool AcceptsPosting => Level == CategoryLevel.Analytical && IsActive && !IsPendingReview;

    private Category()
    {
    }

    public static Category CreateAnalytical(string name, Guid parentId, CategorySection section, int sortOrder = 0)
    {
        return new Category
        {
            Name = RequireName(name),
            ParentId = parentId,
            Level = CategoryLevel.Analytical,
            Section = section,
            IsSystem = false,
            SortOrder = sortOrder,
            IsPendingReview = false
        };
    }

    public static Category CreateSystem(SystemCategoryDefinition definition, Guid? parentId) => new()
    {
        Name = definition.Name,
        Code = definition.Code,
        ParentId = parentId,
        Level = definition.Level,
        Section = definition.Section,
        IsSystem = true,
        SortOrder = definition.SortOrder,
        IsPendingReview = false
    };

    public void SetDisplayNumber(string? displayNumber) => DisplayNumber = displayNumber;

    public void Reparent(Guid parentId, CategorySection section)
    {
        if (IsSystem)
            return;

        ParentId = parentId;
        Section = section;
    }

    public void Rename(string name)
    {
        EnsureNotSystem();
        if (Level != CategoryLevel.Analytical)
            throw new ValidationException("name", "Só contas analíticas podem ser renomeadas.");

        Name = RequireName(name);
    }

    public void SetActive(bool active)
    {
        EnsureNotSystem();
        if (Level != CategoryLevel.Analytical)
            throw new ValidationException("isActive", "Só contas analíticas podem ser ativadas ou desativadas.");

        IsActive = active;
    }

    public void SetSharingScope(SharingScope scope)
    {
        if (Level != CategoryLevel.Analytical)
            throw new ValidationException("sharingScope", "Só contas analíticas têm escopo Família/Pessoal.");

        SharingScope = scope;
    }

    public void MarkFamily() => SharingScope = SharingScope.Family;

    public void MarkPersonal() => SharingScope = SharingScope.Personal;

    public void AssignFamilyIdentity(Guid createdByUsuarioId, Guid familyKey)
    {
        if (IsSystem)
            return;

        if (createdByUsuarioId == Guid.Empty || familyKey == Guid.Empty)
            throw new ValidationException("familyKey", "Identidade da categoria familiar é obrigatória.");

        CreatedByUsuarioId = createdByUsuarioId;
        FamilyKey = familyKey;
    }

    public void MarkPendingReview()
    {
        EnsureAnalytical();
        EnsureNotSystem();
        IsPendingReview = true;
    }

    public void AcceptReview()
    {
        EnsureAnalytical();
        IsPendingReview = false;
    }

    public void SyncFromDefinition(SystemCategoryDefinition definition, Guid? parentId)
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
            throw new SystemCategoryProtectedException();
    }

    public void EnsureAnalytical()
    {
        if (Level != CategoryLevel.Analytical)
            throw new ValidationException("categoryId", "Só contas analíticas recebem valor.");
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("name", "Nome é obrigatório.");

        return name.Trim();
    }
}
