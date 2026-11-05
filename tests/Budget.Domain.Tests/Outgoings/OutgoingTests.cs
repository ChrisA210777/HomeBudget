using Budget.Domain.Common;
using Budget.Domain.Outgoings;

namespace Budget.Domain.Tests.Outgoings;

public class OutgoingTests
{
    private static readonly Guid Household = Guid.NewGuid();
    private static readonly Guid Category = Guid.NewGuid();

    private static OutgoingDetails Details(DateOnly first, OutgoingKind kind = OutgoingKind.Bill) => new()
    {
        Name = "Car insurance",
        Kind = kind,
        CategoryId = Category,
        Priority = OutgoingPriority.Essential,
        FirstPaymentDate = first,
    };

    private static Outgoing Create(decimal amount, PaymentSchedule schedule, DateOnly? first = null) =>
        Outgoing.Create(Household, Details(first ?? new DateOnly(2026, 1, 1)), amount, schedule, "Starting amount");

    private static decimal Month(Outgoing o, int year, int month) => Values.RoundForDisplay(o.MonthlyEquivalentForMonth(year, month));

    // ---- Monthly equivalent (ADR-011) ----

    [Theory]
    [InlineData(480, 1, PaymentUnit.Year, 40)]
    [InlineData(15, 1, PaymentUnit.Week, 65)]
    [InlineData(48, 2, PaymentUnit.Year, 2)]
    [InlineData(100, 1, PaymentUnit.Month, 100)]
    [InlineData(90, 3, PaymentUnit.Month, 30)]
    [InlineData(60, 4, PaymentUnit.Week, 65)]
    [InlineData(169.50, 1, PaymentUnit.Year, 14.13)]
    public void Averages_any_schedule_into_a_monthly_amount(decimal amount, int every, PaymentUnit unit, decimal monthly)
    {
        var outgoing = Create(amount, new PaymentSchedule(every, unit));

        Assert.Equal(monthly, Month(outgoing, 2026, 11));
    }

    [Fact]
    public void Nothing_is_due_before_it_starts()
    {
        var outgoing = Create(480, PaymentSchedule.Yearly, new DateOnly(2026, 12, 1));

        Assert.Equal(0m, Month(outgoing, 2026, 11));
        Assert.Equal(40m, Month(outgoing, 2026, 12));
    }

    [Fact]
    public void Starting_part_way_through_a_month_is_pro_rata()
    {
        var outgoing = Create(300, PaymentSchedule.Monthly, new DateOnly(2026, 11, 16));

        Assert.Equal(150m, Month(outgoing, 2026, 11));
    }

    [Fact]
    public void A_price_rise_mid_month_is_pro_rata()
    {
        var outgoing = Create(300, PaymentSchedule.Monthly);
        outgoing.ChangeAmount(new DateOnly(2026, 11, 16), 330, PaymentSchedule.Monthly, "10% rise");

        Assert.Equal(315m, Month(outgoing, 2026, 11));
        Assert.Equal(330m, Month(outgoing, 2026, 12));
        Assert.Equal(300m, Month(outgoing, 2026, 10));
    }

    [Fact]
    public void Switching_to_annual_payments_doesnt_change_earlier_months()
    {
        var outgoing = Create(50, PaymentSchedule.Monthly);
        outgoing.ChangeAmount(new DateOnly(2026, 12, 1), 540, PaymentSchedule.Yearly, "Paying annually now");

        Assert.Equal(50m, Month(outgoing, 2026, 11));
        Assert.Equal(45m, Month(outgoing, 2026, 12));
    }

    [Fact]
    public void A_pause_with_a_resumption_date_counts_nothing_until_it_resumes()
    {
        var outgoing = Create(900, PaymentSchedule.Monthly);
        outgoing.Pause(new DateOnly(2026, 11, 16), new DateOnly(2027, 2, 1), "Mortgage payment holiday");

        Assert.Equal(450m, Month(outgoing, 2026, 11));
        Assert.Equal(0m, Month(outgoing, 2026, 12));
        Assert.Equal(0m, Month(outgoing, 2027, 1));
        Assert.Equal(900m, Month(outgoing, 2027, 2));
    }

    [Fact]
    public void A_pause_until_further_notice_lasts_until_it_is_corrected()
    {
        var outgoing = Create(20, PaymentSchedule.Monthly);
        var pause = outgoing.Pause(new DateOnly(2026, 11, 1), resumesOn: null, "Contact lenses suspended");

        Assert.Equal(0m, Month(outgoing, 2027, 6));

        outgoing.CorrectStatusPeriod(pause.Id, OutgoingStatusReason.Paused, new DateOnly(2026, 11, 1), new DateOnly(2027, 5, 1), "Contact lenses suspended", "Now resuming in May");

        Assert.Equal(20m, Month(outgoing, 2027, 6));
        Assert.Contains(outgoing.Corrections, c => c.Before.Contains("until further notice", StringComparison.Ordinal) && c.After!.Contains("until 2027-05-01", StringComparison.Ordinal));
    }

    [Fact]
    public void Ending_mid_month_is_pro_rata_and_nothing_after()
    {
        var outgoing = Create(30, PaymentSchedule.Monthly);
        outgoing.End(new DateOnly(2026, 11, 16), "Cancelled");

        Assert.Equal(15m, Month(outgoing, 2026, 11));
        Assert.Equal(0m, Month(outgoing, 2026, 12));
        Assert.Equal(new DateOnly(2026, 11, 16), outgoing.EndsOn);
    }

    [Fact]
    public void Leap_february_is_pro_rata_over_29_days()
    {
        var outgoing = Create(290, PaymentSchedule.Monthly, new DateOnly(2028, 2, 15));

        Assert.Equal(150m, Month(outgoing, 2028, 2));
    }

    // ---- Payment and renewal dates ----

    [Theory]
    [InlineData("2026-11-05", "2026-11-28")]
    [InlineData("2026-11-28", "2026-11-28")]
    [InlineData("2026-11-29", "2026-12-26")]
    public void Next_payment_follows_the_schedule_from_the_first_payment(string today, string expected)
    {
        var outgoing = Create(60, new PaymentSchedule(4, PaymentUnit.Week), new DateOnly(2026, 1, 10));

        Assert.Equal(DateOnly.Parse(expected), outgoing.NextPaymentOnOrAfter(DateOnly.Parse(today)));
    }

    [Theory]
    [InlineData("2026-02-01", "2026-02-28")]
    [InlineData("2026-03-01", "2026-03-31")]
    [InlineData("2028-02-01", "2028-02-29")]
    public void Month_end_payments_are_clamped_without_drifting(string today, string expected)
    {
        var outgoing = Create(10, PaymentSchedule.Monthly, new DateOnly(2026, 1, 31));

        Assert.Equal(DateOnly.Parse(expected), outgoing.NextPaymentOnOrAfter(DateOnly.Parse(today)));
    }

    [Fact]
    public void Next_payment_skips_a_pause_and_none_is_due_once_ended()
    {
        var outgoing = Create(900, PaymentSchedule.Monthly, new DateOnly(2026, 1, 1));
        outgoing.Pause(new DateOnly(2026, 12, 1), new DateOnly(2027, 3, 1), "Payment holiday");

        // 1 Dec falls in the payment holiday, so the next payment is when it resumes.
        Assert.Equal(new DateOnly(2027, 3, 1), outgoing.NextPaymentOnOrAfter(new DateOnly(2026, 11, 5)));
        Assert.Equal(new DateOnly(2026, 11, 1), outgoing.NextPaymentOnOrAfter(new DateOnly(2026, 10, 2)));

        outgoing.End(new DateOnly(2027, 6, 1), "Paid off");
        Assert.Null(outgoing.NextPaymentOnOrAfter(new DateOnly(2027, 6, 2)));
    }

    [Fact]
    public void Renewal_rolls_forward_each_cycle()
    {
        var outgoing = Outgoing.Create(Household, Details(new DateOnly(2025, 3, 3)) with { RenewalDate = new DateOnly(2025, 3, 3), AlertPeriod = AlertPeriod.OneMonth }, 40, PaymentSchedule.Monthly, "Start");

        Assert.Equal(new DateOnly(2027, 3, 3), outgoing.NextRenewalOnOrAfter(new DateOnly(2026, 11, 5)));
        Assert.Equal(new DateOnly(2026, 3, 3), outgoing.NextRenewalOnOrAfter(new DateOnly(2026, 3, 3)));
    }

    [Theory]
    [InlineData("2027-02-02", false)]
    [InlineData("2027-02-03", true)]
    [InlineData("2027-03-03", true)]
    [InlineData("2027-03-04", false)]
    public void Is_coming_up_within_the_alert_period_before_renewal(string today, bool comingUp)
    {
        var outgoing = Outgoing.Create(Household, Details(new DateOnly(2026, 3, 3)) with { RenewalDate = new DateOnly(2026, 3, 3), AlertPeriod = AlertPeriod.OneMonth, AutoRenews = true }, 40, PaymentSchedule.Monthly, "Start");

        Assert.Equal(comingUp, outgoing.IsComingUp(DateOnly.Parse(today)));
    }

    [Fact]
    public void Is_never_coming_up_without_an_alert_or_while_paused()
    {
        var noAlert = Outgoing.Create(Household, Details(new DateOnly(2026, 3, 3)) with { RenewalDate = new DateOnly(2026, 3, 3) }, 40, PaymentSchedule.Monthly, "Start");
        Assert.False(noAlert.IsComingUp(new DateOnly(2027, 3, 1)));

        var paused = Outgoing.Create(Household, Details(new DateOnly(2026, 3, 3)) with { RenewalDate = new DateOnly(2026, 3, 3), AlertPeriod = AlertPeriod.SixMonths }, 40, PaymentSchedule.Monthly, "Start");
        paused.Pause(new DateOnly(2027, 1, 1), null, "Frozen");
        Assert.False(paused.IsComingUp(new DateOnly(2027, 2, 1)));
    }

    // ---- Rules ----

    [Fact]
    public void Allowances_dont_renew()
    {
        Assert.Throws<InvalidOperationException>(() => Outgoing.Create(
            Household, Details(new DateOnly(2026, 1, 1), OutgoingKind.Allowance) with { RenewalDate = new DateOnly(2027, 1, 1) }, 400, PaymentSchedule.Monthly, "Food"));
    }

    [Fact]
    public void Without_a_renewal_there_is_no_alert_or_auto_renew()
    {
        var outgoing = Outgoing.Create(Household, Details(new DateOnly(2026, 1, 1)) with { AlertPeriod = AlertPeriod.OneMonth, AutoRenews = true }, 10, PaymentSchedule.Monthly, "Start");

        Assert.Equal(AlertPeriod.None, outgoing.AlertPeriod);
        Assert.False(outgoing.AutoRenews);
    }

    [Theory]
    [InlineData("netflix.com", "https://netflix.com/")]
    [InlineData("https://www.tvlicensing.co.uk/", "https://www.tvlicensing.co.uk/")]
    [InlineData("  ", null)]
    public void Websites_are_tidied(string input, string? expected)
    {
        var outgoing = Outgoing.Create(Household, Details(new DateOnly(2026, 1, 1)) with { Website = input }, 10, PaymentSchedule.Monthly, "Start");

        Assert.Equal(expected, outgoing.Website);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com")]
    [InlineData("not a website")]
    public void Unsafe_or_invalid_websites_are_rejected(string website)
    {
        Assert.Throws<ArgumentException>(() => Outgoing.Create(Household, Details(new DateOnly(2026, 1, 1)) with { Website = website }, 10, PaymentSchedule.Monthly, "Start"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void How_often_must_be_sensible(int every)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(10, new PaymentSchedule(every, PaymentUnit.Month)));
    }

    [Fact]
    public void Amount_changes_need_a_comment_and_a_unique_date()
    {
        var outgoing = Create(10, PaymentSchedule.Monthly);

        Assert.Throws<ArgumentException>(() => outgoing.ChangeAmount(new DateOnly(2026, 6, 1), 12, PaymentSchedule.Monthly, " "));
        Assert.Throws<InvalidOperationException>(() => outgoing.ChangeAmount(new DateOnly(2026, 1, 1), 12, PaymentSchedule.Monthly, "Same day"));
    }

    [Fact]
    public void Pauses_cant_overlap_or_resume_before_they_start()
    {
        var outgoing = Create(10, PaymentSchedule.Monthly);
        outgoing.Pause(new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1), "Summer");

        Assert.Throws<InvalidOperationException>(() => outgoing.Pause(new DateOnly(2026, 8, 1), null, "Overlap"));
        Assert.Throws<InvalidOperationException>(() => outgoing.Pause(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), "Backwards"));

        // The resumption date isn't part of the pause, so another can start that day.
        outgoing.Pause(new DateOnly(2026, 9, 1), null, "Again");
        Assert.Equal(2, outgoing.StatusPeriods.Count);
    }

    [Fact]
    public void Corrections_and_deletions_keep_the_original_but_not_the_first_amount()
    {
        var outgoing = Create(10, PaymentSchedule.Monthly);
        var rise = outgoing.ChangeAmount(new DateOnly(2026, 6, 1), 12, PaymentSchedule.Monthly, "Rise");

        outgoing.CorrectAmountChange(rise.Id, new DateOnly(2026, 6, 1), 11, PaymentSchedule.Monthly, "Rise", "Typo");
        outgoing.DeleteAmountChange(rise.Id, "Didn't happen");

        Assert.Equal(2, outgoing.Corrections.Count);
        Assert.Equal("£12.00 a month from 2026-06-01 (Rise)", outgoing.Corrections.First().Before);
        Assert.Throws<InvalidOperationException>(() => outgoing.DeleteAmountChange(outgoing.AmountChanges.Single().Id, "Oops"));
        Assert.Throws<KeyNotFoundException>(() => outgoing.DeleteStatusPeriod(Guid.NewGuid(), "Missing"));
    }

    [Fact]
    public void Every_household_gets_the_default_categories_in_order()
    {
        var categories = OutgoingCategory.CreateDefaults(Household);

        Assert.Equal(16, categories.Count);
        Assert.Equal("Housing", categories[0].Name);
        Assert.Equal("Discretionary spending", categories[^1].Name);
        Assert.Equal(categories.OrderBy(c => c.SortOrder).Select(c => c.Name), categories.Select(c => c.Name));
        Assert.All(categories, c => Assert.Equal(Household, c.HouseholdId));
    }

    [Fact]
    public void Category_names_are_trimmed_and_limited()
    {
        var category = OutgoingCategory.Create(Household, "  Kids  ", 1);
        Assert.Equal("Kids", category.Name);

        Assert.Throws<ArgumentException>(() => category.Rename(" "));
        Assert.Throws<ArgumentException>(() => category.Rename(new string('x', 51)));

        category.Hide();
        Assert.True(category.IsHidden);
        category.Show();
        Assert.False(category.IsHidden);
    }
}
