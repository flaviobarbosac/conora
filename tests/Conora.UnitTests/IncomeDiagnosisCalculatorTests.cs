using Conora.Domain.Entities;
using Conora.Domain.Exceptions;
using Conora.Domain.Services;
using Xunit;

namespace Conora.UnitTests;

public class IncomeDiagnosisCalculatorTests
{
    [Fact]
    public void IncomeDiagnosisCalculator_Example_10000()
    {
        var result = IncomeDiagnosisCalculator.Calculate(gross: 10000m, inss: 800m, ir: 1200m, tithe: 800m);

        Assert.Equal(8000m, result.NetSpendable);
        Assert.Equal(800m, result.Tithe);
    }

    [Fact]
    public void Tithe_is_not_subtracted_from_net_spendable()
    {
        var withTithe = IncomeDiagnosisCalculator.Calculate(10000m, 800m, 1200m, 800m);
        var withoutTithe = IncomeDiagnosisCalculator.Calculate(10000m, 800m, 1200m, 0m);

        Assert.Equal(withoutTithe.NetSpendable, withTithe.NetSpendable);
    }

    [Fact]
    public void Deductions_above_gross_are_rejected()
    {
        Assert.Throws<ValidationException>(() => IncomeDiagnosisCalculator.Calculate(1000m, 800m, 400m, 0m));
    }

    [Fact]
    public void IncomeSource_stores_net_spendable_once()
    {
        var source = IncomeSource.Create("Salário", "2026-10", 10000m, 800m, 1200m, 800m);

        Assert.Equal(8000m, source.NetSpendable);
        Assert.Equal(800m, source.Tithe);
    }
}
