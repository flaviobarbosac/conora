using Conora.Domain;
using Conora.Domain.Catalog;
using Conora.Domain.Entities;
using Conora.Domain.Enums;
using Conora.Domain.Exceptions;
using Xunit;

namespace Conora.UnitTests;

public class AccountAndBanksTests
{
    [Fact]
    public void BrazilianBanks_finds_padded_codes()
    {
        Assert.Equal("Nubank", BrazilianBanks.Find("260")!.Name);
        Assert.Equal("Nubank", BrazilianBanks.Find("260")!.Name);
        Assert.Null(BrazilianBanks.Find(null));
        Assert.Null(BrazilianBanks.Find("999"));
        Assert.True(BrazilianBanks.All.Count >= 40);
    }

    [Fact]
    public void FreeTierLimits_allows_only_listed_codes()
    {
        Assert.True(FreeTierLimits.AllowsCategoryCode("ess_rent"));
        Assert.True(FreeTierLimits.AllowsCategoryCode("INC_SALARY"));
        Assert.False(FreeTierLimits.AllowsCategoryCode("OTHER"));
        Assert.False(FreeTierLimits.AllowsCategoryCode(" "));
        Assert.Equal(1, FreeTierLimits.MaxBankAccounts);
    }

    [Fact]
    public void Account_create_update_archive_and_delta()
    {
        var cash = Account.Create("Carteira", AccountKind.Cash);
        Assert.Equal(0m, cash.Balance);
        Assert.Null(cash.BankCode);
        cash.ApplyDelta(50m);
        Assert.Equal(50m, cash.Balance);
        cash.SetArchived(true);
        Assert.True(cash.IsArchived);

        var checking = Account.Create("Conta", AccountKind.Checking, bankCode: "260", agency: "0001", accountNumber: "123", checkDigit: "0");
        Assert.Equal("260", checking.BankCode);
        checking.Update("Conta 2", AccountKind.Checking, "341", "1", "9", "1");
        Assert.Equal("Conta 2", checking.Name);
        Assert.Equal("341", checking.BankCode);
    }

    [Fact]
    public void Account_rejects_blank_name_and_unknown_bank()
    {
        Assert.Throws<ValidationException>(() => Account.Create(" ", AccountKind.Cash));
        Assert.Throws<ValidationException>(() => Account.Create("X", AccountKind.Checking, bankCode: "999"));
    }

    [Fact]
    public void LoginIdentifier_detects_email()
    {
        Assert.True(LoginIdentifier.IsEmail("a@b.com"));
        Assert.False(LoginIdentifier.IsEmail("11999999999"));
    }
}
