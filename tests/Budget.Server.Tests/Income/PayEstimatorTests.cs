using Budget.Domain.Income;
using Budget.Server.Features.Income;
using Budget.Shared.Income;

namespace Budget.Server.Tests.Income;

/// <summary>The form's preview must give the same figures as the server.</summary>
public class PayEstimatorTests
{
    public static TheoryData<AmountBasisDto, PayFrequencyDto> AllCombinations()
    {
        var data = new TheoryData<AmountBasisDto, PayFrequencyDto>();
        foreach (var basis in Enum.GetValues<AmountBasisDto>())
        {
            foreach (var frequency in Enum.GetValues<PayFrequencyDto>())
            {
                data.Add(basis, frequency);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void Fixed_preview_matches_the_server_for_every_combination(AmountBasisDto basis, PayFrequencyDto frequency)
    {
        foreach (var amount in new[] { 1m, 576.92m, 2307.69m, 2500m, 30000m, 123456.78m })
        {
            var server = new FixedPay(amount, basis.ToDomain()).Estimate(frequency.ToDomain());

            var (perPay, perYear) = PayEstimator.Fixed(amount, basis, frequency);

            Assert.Equal(PayCalculator.RoundForDisplay(server.AveragePerPay), perPay);
            Assert.Equal(PayCalculator.RoundForDisplay(server.AverageAnnual), perYear);
        }
    }

    [Theory]
    [InlineData(PayFrequencyDto.Weekly, 450, 23400)]
    [InlineData(PayFrequencyDto.FourWeekly, 2200, 28600)]
    [InlineData(PayFrequencyDto.Monthly, 2400, 28800)]
    public void Variable_preview_annualises_per_pay_amounts(PayFrequencyDto frequency, decimal perPay, decimal expected)
    {
        Assert.Equal(expected, PayEstimator.PerYear(perPay, frequency));
    }

    [Theory]
    [InlineData(2307.69, "GBP", "£2,307.69")]
    [InlineData(30000, "GBP", "£30,000.00")]
    [InlineData(0.5, "EUR", "€0.50")]
    [InlineData(12, "JPY", "JPY 12.00")]
    public void Money_is_formatted_with_symbol_and_pence(decimal amount, string currency, string expected)
    {
        Assert.Equal(expected, Money.Format(amount, currency));
    }
}
