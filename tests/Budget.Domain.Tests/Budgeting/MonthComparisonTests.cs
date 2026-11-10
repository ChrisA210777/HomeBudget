using Budget.Domain.Budgeting;
using Budget.Domain.Income;
using Budget.Domain.Outgoings;

namespace Budget.Domain.Tests.Budgeting;

/// <summary>What changed from the month before, and why (BUD-83, ADR-013).</summary>
public class MonthComparisonTests
{
    private static readonly Guid Household = Guid.NewGuid();
    private static readonly Guid Category = Guid.NewGuid();
    private static readonly Guid Person = Guid.NewGuid();

    private static Outgoing Bill(string name, decimal amount, PaymentSchedule schedule, DateOnly first) =>
        Outgoing.Create(Household, new OutgoingDetails { Name = name, CategoryId = Category, FirstPaymentDate = first }, amount, schedule, "Starting amount");

    private static IncomeStream Salary(decimal amount, DateOnly startsOn) =>
        IncomeStream.Create(Household, Person, "Salary", amount, startsOn, "Starting amount");

    private static IReadOnlyList<BudgetChange> December(IEnumerable<Outgoing> outgoings, IEnumerable<IncomeStream>? income = null) =>
        MonthComparison.Changes(outgoings, income ?? [], 2026, 12);

    [Fact]
    public void Unchanged_amounts_and_annual_bills_are_not_changes()
    {
        // A yearly bill paid in December is spread across every month, so it isn't a change.
        var insurance = Bill("Car insurance", 480m, PaymentSchedule.Yearly, new DateOnly(2026, 12, 14));
        var council = Bill("Council tax", 150m, PaymentSchedule.Monthly, new DateOnly(2026, 4, 1));

        Assert.Empty(MonthComparison.Changes([insurance, council], [Salary(3000m, new DateOnly(2026, 1, 1))], 2027, 1));
    }

    [Fact]
    public void A_price_change_shows_its_comment_and_leaves_less()
    {
        var council = Bill("Council tax", 150m, PaymentSchedule.Monthly, new DateOnly(2026, 4, 1));
        council.ChangeAmount(new DateOnly(2026, 12, 1), 180m, PaymentSchedule.Monthly, "New band");

        var change = Assert.Single(December([council]));

        Assert.Equal((false, "Council tax", 150m, 180m, -30m), (change.IsIncome, change.Name, change.Previous, change.Current, change.Effect));
        Assert.Equal((BudgetChangeReason.AmountChanged, "New band"), (change.Reason, change.Comment));
    }

    [Fact]
    public void New_ended_paused_and_resumed_outgoings_are_explained()
    {
        var gym = Bill("Gym", 30m, PaymentSchedule.Monthly, new DateOnly(2026, 12, 5));
        var phone = Bill("Old phone", 20m, PaymentSchedule.Monthly, new DateOnly(2026, 1, 10));
        phone.End(new DateOnly(2026, 12, 10), "Cancelled");
        var mortgage = Bill("Mortgage", 900m, PaymentSchedule.Monthly, new DateOnly(2026, 1, 1));
        mortgage.PauseFor(new DateOnly(2026, 12, 1), payments: 1, "Payment holiday");

        var changes = December([gym, phone, mortgage]).ToDictionary(c => c.Name);

        Assert.Equal((BudgetChangeReason.New, (string?)null, -30m), (changes["Gym"].Reason, changes["Gym"].Comment, changes["Gym"].Effect));
        Assert.Equal((BudgetChangeReason.Ended, "Cancelled", 20m), (changes["Old phone"].Reason, changes["Old phone"].Comment, changes["Old phone"].Effect));
        Assert.Equal((BudgetChangeReason.Paused, "Payment holiday", 900m), (changes["Mortgage"].Reason, changes["Mortgage"].Comment, changes["Mortgage"].Effect));

        var january = MonthComparison.Changes([mortgage], [], 2027, 1);
        Assert.Equal((BudgetChangeReason.Resumed, -900m), (january.Single().Reason, january.Single().Effect));
    }

    [Fact]
    public void Income_changes_are_explained_and_more_income_leaves_more()
    {
        var chris = Salary(3000m, new DateOnly(2026, 1, 1));
        chris.ChangeAmount(new DateOnly(2026, 12, 1), 3300m, "10% pay rise");
        var jane = IncomeStream.Create(Household, Person, "Jane – salary", 2000m, new DateOnly(2026, 1, 1), "Starting amount");
        jane.AddStatusPeriod(IncomeStatusReason.SickLeave, new DateOnly(2026, 12, 1), null, 500m, "Statutory sick pay");
        var benefit = IncomeStream.Create(Household, Person, "Child Benefit", 110m, new DateOnly(2026, 12, 1), "Starting amount");

        var changes = December([], [chris, jane, benefit]);

        // Biggest effect first.
        Assert.Equal(new[] { "Jane – salary", "Salary", "Child Benefit" }, changes.Select(c => c.Name));
        Assert.All(changes, c => Assert.True(c.IsIncome));
        Assert.Equal((BudgetChangeReason.Reduced, "Statutory sick pay", IncomeStatusReason.SickLeave, -1500m),
            (changes[0].Reason, changes[0].Comment, changes[0].Status, changes[0].Effect));
        Assert.Equal((BudgetChangeReason.AmountChanged, "10% pay rise", 300m), (changes[1].Reason, changes[1].Comment, changes[1].Effect));
        Assert.Equal((BudgetChangeReason.New, 110m), (changes[2].Reason, changes[2].Effect));
    }

    [Fact]
    public void Income_back_to_usual_and_ending_are_explained()
    {
        var jane = IncomeStream.Create(Household, Person, "Jane – salary", 2000m, new DateOnly(2026, 1, 1), "Starting amount");
        jane.AddStatusPeriod(IncomeStatusReason.UnpaidLeave, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), null, "Travelling");
        var chris = Salary(3000m, new DateOnly(2026, 1, 1));
        chris.End(new DateOnly(2026, 12, 1), "Left the job");

        var changes = December([], [jane, chris]).ToDictionary(c => c.Name);

        Assert.Equal((BudgetChangeReason.BackToUsual, "Travelling", IncomeStatusReason.UnpaidLeave, 2000m),
            (changes["Jane – salary"].Reason, changes["Jane – salary"].Comment, changes["Jane – salary"].Status, changes["Jane – salary"].Effect));
        Assert.Equal((BudgetChangeReason.Ended, "Left the job", -3000m), (changes["Salary"].Reason, changes["Salary"].Comment, changes["Salary"].Effect));
    }
}
