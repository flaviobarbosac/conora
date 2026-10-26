namespace Conora.Infrastructure.Billing;

/// <summary>Hotmart course checkout and webhook token. Empty checkout URL hides the link. Empty hottok rejects the webhook.</summary>
public sealed class HotmartOptions
{
    public const string SectionName = "Hotmart";

    public string CheckoutUrl { get; set; } = "";

    /// <summary>Value of the X-HOTMART-HOTTOK header configured in the Hotmart webhook panel.</summary>
    public string Hottok { get; set; } = "";

    /// <summary>Hotmart product id of Leomar Campos's training. Zero disables the extra 60-day grant.</summary>
    public long ProductId { get; set; }
}
