namespace Conora.Infrastructure.Billing;

/// <summary>Hotmart course checkout and webhook token. Empty checkout URL hides the link. Empty hottok rejects the webhook.</summary>
public sealed class HotmartOptions
{
    public const string SectionName = "Hotmart";

    public string CheckoutUrl { get; set; } = "";

    /// <summary>Value of the X-HOTMART-HOTTOK header configured in the Hotmart webhook panel.</summary>
    public string Hottok { get; set; } = "";

    /// <summary>When set, only this product id grants the student trial. Zero accepts any product.</summary>
    public long ProductId { get; set; }
}
