using Conora.Domain.Catalog;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;

namespace Conora.Domain.Entities;

public class Account : ModelBase, ITenantOwned
{
    public Guid UsuarioId { get; set; }
    public string Name { get; private set; } = default!;
    public AccountKind Kind { get; private set; }
    public decimal Balance { get; private set; }
    public bool IsArchived { get; private set; }
    public string? BankCode { get; private set; }
    public string? Agency { get; private set; }
    public string? AccountNumber { get; private set; }
    public string? CheckDigit { get; private set; }

    private Account()
    {
    }

    public static Account Create(
        string name,
        AccountKind kind,
        decimal openingBalance,
        string? bankCode = null,
        string? agency = null,
        string? accountNumber = null,
        string? checkDigit = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("name", "Descrição da conta é obrigatória.");

        var account = new Account
        {
            Name = name.Trim(),
            Kind = kind,
            Balance = openingBalance,
        };
        account.SetBankDetails(kind, bankCode, agency, accountNumber, checkDigit);
        return account;
    }

    public void Update(
        string name,
        AccountKind kind,
        string? bankCode = null,
        string? agency = null,
        string? accountNumber = null,
        string? checkDigit = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("name", "Descrição da conta é obrigatória.");

        Name = name.Trim();
        Kind = kind;
        SetBankDetails(kind, bankCode, agency, accountNumber, checkDigit);
    }

    public void SetArchived(bool archived) => IsArchived = archived;

    /// <summary>Applies a signed delta produced by an entry (or its reversal).</summary>
    public void ApplyDelta(decimal delta) => Balance += delta;

    private void SetBankDetails(AccountKind kind, string? bankCode, string? agency, string? accountNumber, string? checkDigit)
    {
        if (kind == AccountKind.Cash)
        {
            BankCode = null;
            Agency = null;
            AccountNumber = null;
            CheckDigit = null;
            return;
        }

        var code = NormalizeOptional(bankCode, 3);
        if (code is not null)
        {
            code = code.PadLeft(3, '0');
            if (BrazilianBanks.Find(code) is null)
                throw new ValidationException("bankCode", "Código de banco inválido.");
        }

        BankCode = code;
        Agency = NormalizeOptional(agency, 20);
        AccountNumber = NormalizeOptional(accountNumber, 20);
        CheckDigit = NormalizeOptional(checkDigit, 2);
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ValidationException("value", $"Valor não pode ter mais de {maxLength} caracteres.");

        return trimmed;
    }
}
