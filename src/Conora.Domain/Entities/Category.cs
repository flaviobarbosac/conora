using Conora.Domain.Catalog;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class Category : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string Name { get; private set; } = default!;
    public string? Code { get; private set; }
    public CategoryKind Kind { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsEssential { get; private set; }

    private Category()
    {
    }

    public static Category Create(string name, CategoryKind kind, bool isEssential = false)
    {
        var trimmed = RequireName(name);
        if (SystemCategories.IsForbiddenName(trimmed))
            throw new ValidationException("name", "\"Descontos sobre renda\" não é uma categoria lançável.");

        return new Category { Name = trimmed, Kind = kind, IsEssential = isEssential };
    }

    public static Category CreateSystem(SystemCategoryDefinition definition) => new()
    {
        Name = definition.Name,
        Code = definition.Code,
        Kind = definition.Kind,
        IsSystem = true,
        IsEssential = definition.IsEssential
    };

    public void Update(string name, bool isEssential)
    {
        EnsureNotSystem();
        var trimmed = RequireName(name);
        if (SystemCategories.IsForbiddenName(trimmed))
            throw new ValidationException("name", "\"Descontos sobre renda\" não é uma categoria lançável.");

        Name = trimmed;
        IsEssential = isEssential;
    }

    public void SetActive(bool active)
    {
        EnsureNotSystem();
        IsActive = active;
    }

    public void EnsureNotSystem()
    {
        if (IsSystem)
            throw new SystemCategoryProtectedException();
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("name", "Nome é obrigatório.");

        return name.Trim();
    }
}
