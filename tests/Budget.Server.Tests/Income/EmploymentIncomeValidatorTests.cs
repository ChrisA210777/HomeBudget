using Budget.Shared.Income;

namespace Budget.Server.Tests.Income;

/// <summary>The shared validator used by both the form and the API.</summary>
public class EmploymentIncomeValidatorTests
{
    private static readonly SaveEmploymentIncomeRequest ValidFixed = new()
    {
        Name = "Job",
        PayFrequency = PayFrequencyDto.Monthly,
        Kind = IncomeKindDto.Fixed,
        GrossAmount = 2500m,
        AmountBasis = AmountBasisDto.Monthly,
    };

    private static readonly SaveEmploymentIncomeRequest ValidVariable = new()
    {
        Name = "Job",
        PayFrequency = PayFrequencyDto.Weekly,
        Kind = IncomeKindDto.Variable,
        LowestGross = 300m,
        AverageGross = 450m,
        HighestGross = 700m,
    };

    [Fact]
    public void Valid_requests_have_no_errors()
    {
        Assert.Empty(EmploymentIncomeValidator.Validate(ValidFixed));
        Assert.Empty(EmploymentIncomeValidator.Validate(ValidVariable));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_is_required(string name)
    {
        Assert.Contains("Name", EmploymentIncomeValidator.Validate(ValidFixed with { Name = name }).Keys);
    }

    [Fact]
    public void Name_and_employer_have_maximum_lengths()
    {
        var errors = EmploymentIncomeValidator.Validate(ValidFixed with { Name = new string('n', 101), EmployerName = new string('e', 101) });

        Assert.Contains("Name", errors.Keys);
        Assert.Contains("EmployerName", errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-5d)]
    [InlineData(12.345)]
    [InlineData(100_000_001d)]
    public void Fixed_amount_must_be_a_positive_amount_in_pence(double? amount)
    {
        var errors = EmploymentIncomeValidator.Validate(ValidFixed with { GrossAmount = (decimal?)amount });

        Assert.Contains("GrossAmount", errors.Keys);
    }

    [Fact]
    public void Fixed_pay_needs_the_period_the_amount_is_for()
    {
        Assert.Contains("AmountBasis", EmploymentIncomeValidator.Validate(ValidFixed with { AmountBasis = null }).Keys);
    }

    [Fact]
    public void Variable_pay_needs_all_three_amounts()
    {
        var errors = EmploymentIncomeValidator.Validate(ValidVariable with { LowestGross = null, AverageGross = null, HighestGross = null });

        Assert.Equal(new[] { "AverageGross", "HighestGross", "LowestGross" }, errors.Keys.Order());
    }

    [Fact]
    public void Lowest_cannot_be_more_than_average()
    {
        var errors = EmploymentIncomeValidator.Validate(ValidVariable with { LowestGross = 500m });

        Assert.Equal(new[] { "LowestGross" }, errors.Keys);
    }

    [Fact]
    public void Highest_cannot_be_less_than_average()
    {
        var errors = EmploymentIncomeValidator.Validate(ValidVariable with { HighestGross = 400m });

        Assert.Equal(new[] { "HighestGross" }, errors.Keys);
    }

    [Fact]
    public void Fixed_pay_ignores_variable_fields_and_vice_versa()
    {
        Assert.Empty(EmploymentIncomeValidator.Validate(ValidFixed with { LowestGross = 999m, HighestGross = 1m }));
        Assert.Empty(EmploymentIncomeValidator.Validate(ValidVariable with { GrossAmount = -1m, AmountBasis = null }));
    }
}
