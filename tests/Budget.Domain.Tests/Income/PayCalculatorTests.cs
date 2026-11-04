using Budget.Domain.Income;

namespace Budget.Domain.Tests.Income;

public class PayCalculatorTests
{
    [Theory]
    [InlineData(PayFrequency.Weekly, 52)]
    [InlineData(PayFrequency.FourWeekly, 13)]
    [InlineData(PayFrequency.Monthly, 12)]
    public void Pay_frequencies_use_UK_payroll_periods(PayFrequency frequency, int expected)
    {
        Assert.Equal(expected, PayCalculator.PeriodsPerYear(frequency));
    }

    // Every amount basis paid at every frequency, for a £30,000 salary.
    [Theory]
    [InlineData(30000, AmountBasis.Annual, PayFrequency.Weekly, 576.92)]
    [InlineData(30000, AmountBasis.Annual, PayFrequency.FourWeekly, 2307.69)]
    [InlineData(30000, AmountBasis.Annual, PayFrequency.Monthly, 2500.00)]
    [InlineData(2500, AmountBasis.Monthly, PayFrequency.Weekly, 576.92)]
    [InlineData(2500, AmountBasis.Monthly, PayFrequency.FourWeekly, 2307.69)]
    [InlineData(2500, AmountBasis.Monthly, PayFrequency.Monthly, 2500.00)]
    [InlineData(2307.69, AmountBasis.FourWeekly, PayFrequency.Weekly, 576.92)]
    [InlineData(2307.69, AmountBasis.FourWeekly, PayFrequency.FourWeekly, 2307.69)]
    [InlineData(2307.69, AmountBasis.FourWeekly, PayFrequency.Monthly, 2500.00)]
    [InlineData(576.92, AmountBasis.Weekly, PayFrequency.Weekly, 576.92)]
    [InlineData(576.92, AmountBasis.Weekly, PayFrequency.FourWeekly, 2307.68)]
    [InlineData(576.92, AmountBasis.Weekly, PayFrequency.Monthly, 2499.99)]
    public void Fixed_pay_per_pay_period_for_every_basis_and_frequency(
        decimal amount, AmountBasis basis, PayFrequency frequency, decimal expectedPerPay)
    {
        var estimate = new FixedPay(amount, basis).Estimate(frequency);

        Assert.Equal(expectedPerPay, PayCalculator.RoundForDisplay(estimate.AveragePerPay));
        Assert.Equal(estimate.AveragePerPay, estimate.LowPerPay);
        Assert.Equal(estimate.AveragePerPay, estimate.HighPerPay);
    }

    [Theory]
    [InlineData(30000, AmountBasis.Annual, 30000)]
    [InlineData(2500, AmountBasis.Monthly, 30000)]
    [InlineData(2000, AmountBasis.FourWeekly, 26000)]
    [InlineData(500, AmountBasis.Weekly, 26000)]
    public void Fixed_pay_annual_equivalent(decimal amount, AmountBasis basis, decimal expectedAnnual)
    {
        var estimate = new FixedPay(amount, basis).Estimate(PayFrequency.Monthly);

        Assert.Equal(expectedAnnual, estimate.AverageAnnual);
    }

    [Fact]
    public void Thirty_thousand_a_year_paid_four_weekly_is_2307_69_per_pay()
    {
        var estimate = new FixedPay(30000m, AmountBasis.Annual).Estimate(PayFrequency.FourWeekly);

        Assert.Equal(2307.69m, PayCalculator.RoundForDisplay(estimate.AveragePerPay));
        Assert.Equal(30000m, estimate.AverageAnnual);
    }

    [Fact]
    public void Variable_pay_is_per_pay_period_with_annual_ranges()
    {
        var estimate = new VariablePay(1800m, 2200m, 2900m).Estimate(PayFrequency.FourWeekly);

        Assert.Equal(1800m, estimate.LowPerPay);
        Assert.Equal(2200m, estimate.AveragePerPay);
        Assert.Equal(2900m, estimate.HighPerPay);
        Assert.Equal(23400m, estimate.LowAnnual);
        Assert.Equal(28600m, estimate.AverageAnnual);
        Assert.Equal(37700m, estimate.HighAnnual);
    }

    [Theory]
    [InlineData(2.345, 2.35)]
    [InlineData(2.335, 2.34)]
    [InlineData(-2.345, -2.35)]
    public void Display_rounding_rounds_halves_away_from_zero(decimal amount, decimal expected)
    {
        Assert.Equal(expected, PayCalculator.RoundForDisplay(amount));
    }

    [Fact]
    public void Monthly_figure_is_a_twelfth_of_the_annual()
    {
        Assert.Equal(2500m, PayCalculator.PerMonth(30000m));
    }
}
