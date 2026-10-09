using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class WhatsAppLink : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string PhoneE164 { get; private set; } = default!;
    public DateTime LinkedAt { get; private set; }

    private WhatsAppLink()
    {
    }

    public static string NormalizePhone(string phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is < 10 or > 15)
            throw new ValidationException("phone", "Informe um telefone válido com DDI e DDD.");

        return "+" + digits;
    }

    public static WhatsAppLink Create(string phone) => new()
    {
        PhoneE164 = NormalizePhone(phone),
        LinkedAt = DateTime.UtcNow
    };
}
