namespace Conora.Domain.Catalog;

public sealed record BrazilianBank(string Code, string Name);

/// <summary>COMPE codes focused on Southeast banks plus major national institutions.</summary>
public static class BrazilianBanks
{
    public static readonly IReadOnlyList<BrazilianBank> All =
    [
        new("001", "Banco do Brasil"),
        new("021", "Banestes"),
        new("033", "Santander"),
        new("041", "Banrisul"),
        new("070", "BRB"),
        new("077", "Banco Inter"),
        new("085", "Ailos"),
        new("097", "Credisis"),
        new("102", "XP Investimentos"),
        new("104", "Caixa Econômica Federal"),
        new("121", "Agibank"),
        new("136", "Unicred"),
        new("197", "Stone"),
        new("208", "BTG Pactual"),
        new("212", "Banco Original"),
        new("218", "BS2"),
        new("237", "Bradesco"),
        new("246", "Banco ABC Brasil"),
        new("254", "Paraná Banco"),
        new("260", "Nubank"),
        new("290", "PagBank"),
        new("318", "Banco BMG"),
        new("323", "Mercado Pago"),
        new("336", "C6 Bank"),
        new("341", "Itaú Unibanco"),
        new("380", "PicPay"),
        new("389", "Banco Mercantil do Brasil"),
        new("422", "Banco Safra"),
        new("456", "Banco MUFG Brasil"),
        new("505", "UBS Brasil"),
        new("600", "Banco Luso Brasileiro"),
        new("604", "Banco Industrial"),
        new("610", "Banco VR"),
        new("611", "Banco Paulista"),
        new("612", "Banco Guanabara"),
        new("623", "Banco Pan"),
        new("630", "Smartbank"),
        new("633", "Banco Rendimento"),
        new("634", "Banco Triângulo"),
        new("637", "Banco Sofisa"),
        new("643", "Banco Pine"),
        new("655", "Neon"),
        new("707", "Banco Daycoval"),
        new("739", "Banco Cetelem"),
        new("741", "Banco Ribeirão Preto"),
        new("745", "Citibank"),
        new("746", "Banco Modal"),
        new("747", "Rabobank"),
        new("748", "Sicredi"),
        new("751", "Scotiabank Brasil"),
        new("752", "BNP Paribas Brasil"),
        new("755", "Bank of America Merrill Lynch"),
        new("756", "Sicoob"),
    ];

    public static BrazilianBank? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalized = code.Trim().PadLeft(3, '0');
        return All.FirstOrDefault(b => b.Code == normalized);
    }
}
