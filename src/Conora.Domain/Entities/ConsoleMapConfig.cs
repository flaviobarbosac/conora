namespace Conora.Domain.Entities;

/// <summary>Single row that controls how user pins look on the console map.</summary>
public class ConsoleMapConfig : ModelBase
{
    public string PinLabelField { get; set; } = "nome";
    public string? PinLabelText { get; set; }
    public bool ShowLabel { get; set; } = true;
    public string VisibleFieldsJson { get; set; } = "[]";
}
