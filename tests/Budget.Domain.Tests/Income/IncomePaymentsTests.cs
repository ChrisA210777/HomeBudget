using Budget.Domain.Income;
using Budget.Domain.Outgoings;

namespace Budget.Domain.Tests.Income;

/// <summary>When income arrives, for the cash flow (BUD-92).</summary>
public class IncomePaymentsTests
{
    private static IncomeStream Salary(decimal amount = 3000m, PaymentSchedule? schedule = null) =>
        IncomeStream.Create(Guid.NewGuid(), Guid.NewGuid(), "Salary", amount, new DateOnly(2026, 1, 25), "Starting amount", schedule ?? PaymentSchedule.Monthly);

    [Fact]
    public void Arrives_on_its_schedule_from_its_first_date_at_the_amount_in_force()
    {
        var salary = Salary();
        salary.ChangeAmount(new DateOnly(2026, 3, 1), 3300m, "Pay rise");

        Assert.Equal(
            new[] { (new DateOnly(2026, 1, 25), 3000m), (new DateOnly(2026, 2, 25), 3000m), (new DateOnly(2026, 3, 25), 3300m) },
            salary.PaymentsBetween(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)).Select(p => (p.Date, p.Amount)));
    }

    [Fact]
    public void Every_four_weeks_is_counted_in_weeks()
    {
        var benefit = Salary(102.20m, new PaymentSchedule(4, PaymentUnit.Week));

        Assert.Equal(
            new[] { new DateOnly(2026, 2, 22), new DateOnly(2026, 3, 22) },
            benefit.PaymentsBetween(new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 31)).Select(p => p.Date));
    }

    [Fact]
    public void Leave_scales_the_payment_and_nothing_arrives_once_it_has_ended()
    {
        var salary = Salary();
        salary.AddStatusPeriod(IncomeStatusReason.SickLeave, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), 1500m, "Statutory sick pay");
        salary.AddStatusPeriod(IncomeStatusReason.UnpaidLeave, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), null, "Unpaid");
        salary.End(new DateOnly(2026, 5, 1), "Left");

        Assert.Equal(
            new[] { (new DateOnly(2026, 2, 25), 1500m), (new DateOnly(2026, 4, 25), 3000m) },
            salary.PaymentsBetween(new DateOnly(2026, 2, 1), new DateOnly(2026, 8, 31)).Select(p => (p.Date, p.Amount)));
    }
}
