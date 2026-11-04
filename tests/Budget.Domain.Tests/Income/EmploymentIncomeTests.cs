using Budget.Domain.Income;

namespace Budget.Domain.Tests.Income;

public class EmploymentIncomeTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();

    [Fact]
    public void New_fixed_income_keeps_the_amount_exactly_as_entered()
    {
        var memberId = Guid.NewGuid();

        var income = EmploymentIncome.Create(
            HouseholdId, "  Sam – Acme Ltd  ", " Acme Ltd ", memberId, PayFrequency.FourWeekly, new FixedPay(30000m, AmountBasis.Annual));

        Assert.Equal(HouseholdId, income.HouseholdId);
        Assert.Equal(IncomeStreamType.Employment, income.Type);
        Assert.Equal("Sam – Acme Ltd", income.Name);
        Assert.Equal("Acme Ltd", income.EmployerName);
        Assert.Equal(memberId, income.MemberUserId);
        Assert.Equal(PayFrequency.FourWeekly, income.PayFrequency);
        Assert.Equal(IncomeKind.Fixed, income.Kind);
        Assert.Equal(30000m, income.GrossAmount);
        Assert.Equal(AmountBasis.Annual, income.AmountBasis);
        Assert.Null(income.AverageGross);
        Assert.True(income.IsActive);
        Assert.Equal(new FixedPay(30000m, AmountBasis.Annual), income.Pay);
    }

    [Fact]
    public void New_variable_income_stores_per_pay_figures()
    {
        var income = EmploymentIncome.Create(
            HouseholdId, "Overtime job", null, null, PayFrequency.Weekly, new VariablePay(300m, 450m, 700m));

        Assert.Equal(IncomeKind.Variable, income.Kind);
        Assert.Equal(300m, income.LowestGross);
        Assert.Equal(450m, income.AverageGross);
        Assert.Equal(700m, income.HighestGross);
        Assert.Null(income.GrossAmount);
        Assert.Null(income.AmountBasis);
        Assert.Null(income.EmployerName);
        Assert.Null(income.MemberUserId);
        Assert.Equal(450m * 52, income.Estimate().AverageAnnual);
    }

    [Fact]
    public void Switching_from_fixed_to_variable_clears_the_fixed_amount()
    {
        var income = EmploymentIncome.Create(
            HouseholdId, "Job", null, null, PayFrequency.Monthly, new FixedPay(2500m, AmountBasis.Monthly));

        income.Update("Job", null, null, PayFrequency.Monthly, new VariablePay(2000m, 2400m, 3000m));

        Assert.Equal(IncomeKind.Variable, income.Kind);
        Assert.Null(income.GrossAmount);
        Assert.Null(income.AmountBasis);
        Assert.Equal(2400m, income.AverageGross);
    }

    [Fact]
    public void Switching_from_variable_to_fixed_clears_the_variable_amounts()
    {
        var income = EmploymentIncome.Create(
            HouseholdId, "Job", null, null, PayFrequency.Monthly, new VariablePay(2000m, 2400m, 3000m));

        income.Update("Job", null, null, PayFrequency.Weekly, new FixedPay(600m, AmountBasis.Weekly));

        Assert.Equal(IncomeKind.Fixed, income.Kind);
        Assert.Null(income.LowestGross);
        Assert.Null(income.AverageGross);
        Assert.Null(income.HighestGross);
        Assert.Equal(600m, income.GrossAmount);
        Assert.Equal(PayFrequency.Weekly, income.PayFrequency);
    }

    [Fact]
    public void Deactivated_income_can_be_reactivated()
    {
        var income = EmploymentIncome.Create(
            HouseholdId, "Job", null, null, PayFrequency.Monthly, new FixedPay(2500m, AmountBasis.Monthly));

        income.Deactivate();
        Assert.False(income.IsActive);

        income.Reactivate();
        Assert.True(income.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Name_is_required(string name)
    {
        Assert.Throws<ArgumentException>(() => EmploymentIncome.Create(
            HouseholdId, name, null, null, PayFrequency.Monthly, new FixedPay(1m, AmountBasis.Monthly)));
    }

    [Fact]
    public void Name_and_employer_have_maximum_lengths()
    {
        var pay = new FixedPay(1m, AmountBasis.Monthly);

        Assert.Throws<ArgumentException>(() => EmploymentIncome.Create(
            HouseholdId, new string('n', 101), null, null, PayFrequency.Monthly, pay));
        Assert.Throws<ArgumentException>(() => EmploymentIncome.Create(
            HouseholdId, "Job", new string('e', 101), null, PayFrequency.Monthly, pay));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.234)]
    [InlineData(100_000_000.01)]
    public void Fixed_amount_must_be_positive_with_at_most_two_decimal_places(decimal amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedPay(amount, AmountBasis.Annual));
    }

    [Theory]
    [InlineData(500, 400, 600)]
    [InlineData(300, 700, 600)]
    public void Variable_pay_must_be_lowest_up_to_average_up_to_highest(decimal lowest, decimal average, decimal highest)
    {
        Assert.Throws<ArgumentException>(() => new VariablePay(lowest, average, highest));
    }

    [Fact]
    public void Variable_pay_can_be_the_same_every_time()
    {
        var pay = new VariablePay(500m, 500m, 500m);

        Assert.Equal(500m, pay.AverageGross);
    }
}
