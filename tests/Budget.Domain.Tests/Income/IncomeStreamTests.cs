using Budget.Domain.Income;
using Budget.Domain.Outgoings;

namespace Budget.Domain.Tests.Income;

public class IncomeStreamTests
{
    private static readonly Guid HouseholdId = Guid.NewGuid();
    private static readonly Guid PersonId = Guid.NewGuid();

    private static IncomeStream Salary(decimal amount = 3000m, int year = 2026, int month = 1, int day = 1) =>
        IncomeStream.Create(HouseholdId, PersonId, "Chris – salary", amount, new DateOnly(year, month, day), "Starting amount");

    private static decimal Month(IncomeStream stream, int year, int month) =>
        IncomeTimeline.RoundForDisplay(stream.AmountForMonth(year, month));

    [Fact]
    public void Four_weekly_income_is_averaged_into_the_month()
    {
        // Child Benefit: £102.20 every 4 weeks is 13 payments a year, so £102.20 × 13 ÷ 12 a month.
        var benefit = IncomeStream.Create(HouseholdId, PersonId, "Child Benefit", 102.20m, new DateOnly(2026, 1, 5), "Starting amount",
            new PaymentSchedule(4, PaymentUnit.Week));

        Assert.Equal(110.72m, Month(benefit, 2026, 11));
        Assert.Equal(110.72m, Month(benefit, 2026, 12));
        Assert.Equal("102.20 every 4 weeks from 2026-01-05 (Starting amount)", benefit.AmountChanges.Single().Describe());
    }

    [Fact]
    public void Leave_is_still_pro_rated_by_day_on_the_monthly_equivalent()
    {
        var weekly = IncomeStream.Create(HouseholdId, PersonId, "Weekly wage", 600m, new DateOnly(2026, 1, 5), "Starting amount", PaymentSchedule.Weekly);
        weekly.AddStatusPeriod(IncomeStatusReason.UnpaidLeave, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 15), null, "Unpaid leave");

        // £600 a week = £2,600 a month; half of November unpaid.
        Assert.Equal(1300m, Month(weekly, 2026, 11));
    }

    [Fact]
    public void Switching_from_monthly_to_annual_is_a_new_amount_change()
    {
        var stream = Salary(3000m);
        stream.ChangeAmount(new DateOnly(2026, 12, 1), 6000m, "Annual bonus scheme instead", PaymentSchedule.Yearly);

        Assert.Equal(3000m, Month(stream, 2026, 11));
        Assert.Equal(500m, Month(stream, 2026, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.ChangeAmount(new DateOnly(2027, 1, 1), 1m, "Bad", new PaymentSchedule(0, PaymentUnit.Week)));
    }

    [Fact]
    public void A_whole_month_with_no_changes_is_exactly_the_monthly_amount()
    {
        var stream = Salary(2345.67m);

        Assert.Equal(2345.67m, stream.AmountForMonth(2026, 3));
        Assert.Equal(2345.67m, stream.AmountForMonth(2026, 2));
    }

    [Fact]
    public void There_is_no_income_before_it_starts()
    {
        var stream = Salary(year: 2026, month: 6);

        Assert.Equal(0m, Month(stream, 2026, 5));
        Assert.Equal(0m, stream.AmountOn(new DateOnly(2026, 5, 31)));
        Assert.Equal(3000m, stream.AmountOn(new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Starting_part_way_through_a_month_is_pro_rata()
    {
        // Starts on the 16th of a 30-day month: 15 days of income.
        var stream = Salary(3000m, 2026, 11, 16);

        Assert.Equal(1500m, Month(stream, 2026, 11));
    }

    [Fact]
    public void A_pay_rise_part_way_through_a_month_is_pro_rata()
    {
        var stream = Salary(3000m);
        stream.ChangeAmount(new DateOnly(2026, 11, 16), 3300m, "10% pay rise");

        // 15 days at 3,000 and 15 days at 3,300.
        Assert.Equal(3150m, Month(stream, 2026, 11));
        Assert.Equal(3000m, Month(stream, 2026, 10));
        Assert.Equal(3300m, Month(stream, 2026, 12));
    }

    [Fact]
    public void Future_changes_apply_from_their_date()
    {
        var stream = Salary(3000m);
        stream.ChangeAmount(new DateOnly(2027, 4, 1), 3090m, "3% pay rise agreed for April");

        Assert.Equal(3000m, Month(stream, 2027, 3));
        Assert.Equal(3090m, Month(stream, 2027, 4));
    }

    [Fact]
    public void Two_weeks_unpaid_leave_halves_a_thirty_day_month()
    {
        var stream = Salary(3000m);
        stream.AddStatusPeriod(IncomeStatusReason.UnpaidLeave, new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 17), null, "Unpaid leave");

        Assert.Equal(1500m, Month(stream, 2026, 11));
        Assert.Equal(0m, stream.AmountOn(new DateOnly(2026, 11, 10)));
        Assert.Equal(3000m, stream.AmountOn(new DateOnly(2026, 11, 18)));
    }

    [Fact]
    public void Reduced_parental_pay_across_month_boundaries()
    {
        var stream = Salary(3100m);
        stream.AddStatusPeriod(
            IncomeStatusReason.ParentalLeave, new DateOnly(2026, 12, 17), new DateOnly(2027, 2, 14), 620m, "Statutory paternity and shared parental pay");

        // December: 16 days at 3,100 and 15 days at 620 out of 31.
        Assert.Equal(1900m, Month(stream, 2026, 12));

        // January: all 31 days at the reduced amount.
        Assert.Equal(620m, Month(stream, 2027, 1));

        // February 2027 has 28 days: 14 reduced, 14 normal.
        Assert.Equal(1860m, Month(stream, 2027, 2));
        Assert.Equal(3100m, Month(stream, 2027, 3));
    }

    [Fact]
    public void Leave_until_further_notice_continues_into_every_later_month()
    {
        var stream = Salary(3000m);
        stream.AddStatusPeriod(IncomeStatusReason.SickLeave, new DateOnly(2026, 11, 1), null, 470m, "Statutory sick pay");

        Assert.Equal(470m, Month(stream, 2026, 11));
        Assert.Equal(470m, Month(stream, 2027, 11));
    }

    [Fact]
    public void An_ended_income_stops_from_its_end_date()
    {
        var stream = Salary(3000m);
        stream.End(new DateOnly(2026, 11, 16), "Left the job");

        Assert.Equal(1500m, Month(stream, 2026, 11));
        Assert.Equal(0m, Month(stream, 2026, 12));
        Assert.Equal(new DateOnly(2026, 11, 16), stream.EndsOn);
    }

    [Fact]
    public void Leap_year_february_has_29_days()
    {
        var stream = Salary(2900m, 2028, 2, 15);

        // 15 of February 2028's 29 days.
        Assert.Equal(1500m, Month(stream, 2028, 2));
    }

    [Fact]
    public void Only_one_amount_change_per_date()
    {
        var stream = Salary();

        Assert.Throws<InvalidOperationException>(() => stream.ChangeAmount(new DateOnly(2026, 1, 1), 3100m, "Duplicate"));
    }

    [Fact]
    public void Leave_periods_cannot_overlap()
    {
        var stream = Salary();
        stream.AddStatusPeriod(IncomeStatusReason.Holiday, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 14), null, "Unpaid holiday");

        Assert.Throws<InvalidOperationException>(() =>
            stream.AddStatusPeriod(IncomeStatusReason.SickLeave, new DateOnly(2026, 8, 14), new DateOnly(2026, 8, 20), null, "Sick"));
        Assert.Throws<InvalidOperationException>(() => stream.End(new DateOnly(2026, 7, 1), "Ended"));

        // The day after is fine.
        stream.AddStatusPeriod(IncomeStatusReason.SickLeave, new DateOnly(2026, 8, 15), new DateOnly(2026, 8, 20), null, "Sick");
    }

    [Fact]
    public void A_period_cannot_end_before_it_starts()
    {
        var stream = Salary();

        Assert.Throws<ArgumentException>(() =>
            stream.AddStatusPeriod(IncomeStatusReason.Other, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 9), null, "Backwards"));
    }

    [Fact]
    public void Comments_are_required()
    {
        Assert.Throws<ArgumentException>(() => IncomeStream.Create(HouseholdId, PersonId, "Job", 1m, new DateOnly(2026, 1, 1), " "));
        Assert.Throws<ArgumentException>(() => Salary().ChangeAmount(new DateOnly(2026, 2, 1), 1m, ""));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.234)]
    public void Amounts_must_be_positive_pounds_and_pence(double amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Salary((decimal)amount));
    }

    [Fact]
    public void Correcting_an_amount_keeps_the_original_and_the_reason()
    {
        var stream = Salary(3000m);
        var rise = stream.ChangeAmount(new DateOnly(2026, 4, 1), 3200m, "Pay rise");

        stream.CorrectAmountChange(rise.Id, new DateOnly(2026, 4, 1), 3100m, "Pay rise", "Typed the wrong figure");

        Assert.Equal(3100m, Month(stream, 2026, 5));
        var correction = Assert.Single(stream.Corrections);
        Assert.Equal(IncomeCorrectionAction.Edited, correction.Action);
        Assert.Equal(IncomeEntryType.AmountChange, correction.EntryType);
        Assert.Equal(rise.Id, correction.EntryId);
        Assert.Contains("3,200.00", correction.Before, StringComparison.Ordinal);
        Assert.Contains("3,100.00", correction.After, StringComparison.Ordinal);
        Assert.Equal("Typed the wrong figure", correction.Reason);
    }

    [Fact]
    public void Deleting_a_change_keeps_a_record_and_a_reason_is_required()
    {
        var stream = Salary(3000m);
        var rise = stream.ChangeAmount(new DateOnly(2026, 4, 1), 3200m, "Pay rise");

        Assert.Throws<ArgumentException>(() => stream.DeleteAmountChange(rise.Id, " "));
        stream.DeleteAmountChange(rise.Id, "Recorded on the wrong income");

        Assert.Equal(3000m, Month(stream, 2026, 5));
        var correction = Assert.Single(stream.Corrections);
        Assert.Equal(IncomeCorrectionAction.Deleted, correction.Action);
        Assert.Null(correction.After);
    }

    [Fact]
    public void The_first_amount_cannot_be_deleted()
    {
        var stream = Salary();

        Assert.Throws<InvalidOperationException>(() => stream.DeleteAmountChange(stream.AmountChanges.Single().Id, "Oops"));
    }

    [Fact]
    public void Correcting_and_deleting_periods_are_recorded()
    {
        var stream = Salary(3000m);
        var leave = stream.AddStatusPeriod(IncomeStatusReason.SickLeave, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 6), null, "Flu");

        stream.CorrectStatusPeriod(leave.Id, IncomeStatusReason.SickLeave, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 13), null, "Flu", "Off for two weeks, not one");
        stream.DeleteStatusPeriod(leave.Id, "Was paid after all");

        Assert.Equal(3000m, Month(stream, 2026, 3));
        Assert.Equal(
            new[] { IncomeCorrectionAction.Edited, IncomeCorrectionAction.Deleted },
            stream.Corrections.Select(c => c.Action));
    }

    [Fact]
    public void An_ended_income_has_no_end_date_or_reduced_amount()
    {
        var stream = Salary();

        Assert.Throws<ArgumentException>(() =>
            stream.AddStatusPeriod(IncomeStatusReason.Ended, new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 1), null, "Ended"));
    }

    [Fact]
    public void Name_and_person_can_change()
    {
        var stream = Salary();
        var newPerson = Guid.NewGuid();

        stream.Rename(" Chris – new job ");
        stream.ChangePerson(newPerson);

        Assert.Equal("Chris – new job", stream.Name);
        Assert.Equal(newPerson, stream.PersonId);
        Assert.Throws<ArgumentException>(() => stream.ChangePerson(Guid.Empty));
    }
}
