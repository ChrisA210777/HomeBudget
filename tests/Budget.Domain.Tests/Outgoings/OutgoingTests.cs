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
    public void The_month_it_starts_counts_in_full()
    {
        var monthly = Create(300, PaymentSchedule.Monthly, new DateOnly(2026, 11, 16));
        var weekly = Create(15, PaymentSchedule.Weekly, new DateOnly(2026, 11, 26));

        Assert.Equal(300m, Month(monthly, 2026, 11));
        Assert.Equal(65m, Month(weekly, 2026, 11));
    }

    [Fact]
    public void A_price_change_counts_from_the_first_payment_at_the_new_price()
    {
        // Paid on the 1st, so November's payment is still £300.
        var outgoing = Create(300, PaymentSchedule.Monthly);
        outgoing.ChangeAmount(new DateOnly(2026, 11, 16), 330, PaymentSchedule.Monthly, "10% rise");

        Assert.Equal(300m, Month(outgoing, 2026, 10));
        Assert.Equal(300m, Month(outgoing, 2026, 11));
        Assert.Equal(330m, Month(outgoing, 2026, 12));
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
    public void A_monthly_pause_skips_whole_months()
    {
        var outgoing = Create(900, PaymentSchedule.Monthly);
        var pause = outgoing.PauseFor(new DateOnly(2026, 12, 1), payments: 2, "Mortgage payment holiday");

        Assert.Equal(new DateOnly(2027, 2, 1), pause.ResumesOn);
        Assert.Equal(900m, Month(outgoing, 2026, 11));
        Assert.Equal(0m, Month(outgoing, 2026, 12));
        Assert.Equal(0m, Month(outgoing, 2027, 1));
        Assert.Equal(900m, Month(outgoing, 2027, 2));
    }

    [Fact]
    public void Skipping_a_years_payment_stops_twelve_months_of_setting_aside()
    {
        var outgoing = Create(480, PaymentSchedule.Yearly, new DateOnly(2026, 3, 14));
        var pause = outgoing.PauseFor(new DateOnly(2027, 3, 14), payments: 1, "Not renewing this year");

        Assert.Equal(new DateOnly(2028, 3, 14), pause.ResumesOn);
        Assert.Equal(40m, Month(outgoing, 2027, 2));
        Assert.Equal(0m, Month(outgoing, 2027, 3));
        Assert.Equal(0m, Month(outgoing, 2028, 2));
        Assert.Equal(40m, Month(outgoing, 2028, 3));
    }

    [Fact]
    public void A_weekly_pause_takes_off_the_share_of_that_months_payments()
    {
        // Paid on Mondays: 2, 9, 16, 23 and 30 November 2026. Two of the five are skipped.
        var outgoing = Create(15, PaymentSchedule.Weekly, new DateOnly(2026, 1, 5));
        outgoing.PauseFor(new DateOnly(2026, 11, 9), payments: 2, "Away");

        Assert.Equal(39m, Month(outgoing, 2026, 11));
        Assert.Equal(65m, Month(outgoing, 2026, 12));
    }

    [Fact]
    public void Payments_further_apart_than_a_month_use_the_latest_one()
    {
        // Every 6 weeks from 5 Jan 2026: 26 Oct, then 7 Dec, so nothing is due in November.
        var outgoing = Create(60, new PaymentSchedule(6, PaymentUnit.Week), new DateOnly(2026, 1, 5));

        Assert.Equal(new[] { new DateOnly(2026, 10, 26) }, outgoing.CountingDaysIn(2026, 11));
        Assert.Equal(Values.RoundForDisplay(60m * 52 / 12 / 6), Month(outgoing, 2026, 11));

        outgoing.PauseFor(new DateOnly(2026, 10, 26), payments: 1, "Skipped");
        Assert.Equal(0m, Month(outgoing, 2026, 11));
        Assert.Equal(Values.RoundForDisplay(60m * 52 / 12 / 6), Month(outgoing, 2026, 12));
    }

    [Fact]
    public void A_pause_knows_how_many_payments_it_skips()
    {
        var outgoing = Create(15, PaymentSchedule.Weekly, new DateOnly(2026, 1, 5));
        var skipping = outgoing.PauseFor(new DateOnly(2026, 11, 9), payments: 3, "Away");
        var open = outgoing.Pause(new DateOnly(2027, 1, 4), null, "Stopped for now");

        Assert.Equal(new DateOnly(2026, 11, 30), skipping.ResumesOn);
        Assert.Equal(3, outgoing.PaymentsSkippedBy(skipping));
        Assert.Null(outgoing.PaymentsSkippedBy(open));
    }

    [Fact]
    public void Upcoming_payment_dates_follow_the_schedule()
    {
        var outgoing = Create(10, PaymentSchedule.Monthly, new DateOnly(2026, 1, 31));

        Assert.Equal(
            new[] { new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 31), new DateOnly(2027, 2, 28) },
            outgoing.PaymentDatesFrom(new DateOnly(2026, 11, 5), 4));
        Assert.Equal(new DateOnly(2026, 1, 31), outgoing.PaymentDatesFrom(new DateOnly(2025, 6, 1), 1)[0]);
    }

    [Fact]
    public void Correcting_the_first_amount_moves_the_first_payment_date()
    {
        var outgoing = Create(10, PaymentSchedule.Monthly, new DateOnly(2026, 1, 1));
        var first = outgoing.AmountChanges.Single();
        outgoing.ChangeAmount(new DateOnly(2026, 6, 1), 12, PaymentSchedule.Monthly, "Rise");

        outgoing.CorrectAmountChange(first.Id, new DateOnly(2026, 1, 15), 10, PaymentSchedule.Monthly, "Starting amount", "Wrong day");

        Assert.Equal(new DateOnly(2026, 1, 15), outgoing.FirstPaymentDate);
        Assert.Throws<InvalidOperationException>(() =>
            outgoing.CorrectAmountChange(first.Id, new DateOnly(2026, 7, 1), 10, PaymentSchedule.Monthly, "Starting amount", "Too late"));
        var rise = outgoing.AmountChanges.Single(c => c.Amount == 12);
        Assert.Throws<InvalidOperationException>(() =>
            outgoing.CorrectAmountChange(rise.Id, new DateOnly(2026, 1, 1), 12, PaymentSchedule.Monthly, "Rise", "Too early"));
    }

    [Fact]
    public void Editing_details_doesnt_move_the_first_payment()
    {
        var outgoing = Create(10, PaymentSchedule.Monthly, new DateOnly(2026, 1, 1));

        outgoing.UpdateDetails(Details(new DateOnly(2026, 3, 1)) with { Name = "Renamed" });

        Assert.Equal("Renamed", outgoing.Name);
        Assert.Equal(new DateOnly(2026, 1, 1), outgoing.FirstPaymentDate);
    }

    [Fact]
    public void Pauses_start_and_resume_on_payment_dates()
    {
        var outgoing = Create(900, PaymentSchedule.Monthly);

        Assert.Throws<InvalidOperationException>(() => outgoing.Pause(new DateOnly(2026, 11, 16), null, "Mid-month"));
        Assert.Throws<InvalidOperationException>(() => outgoing.Pause(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 16), "Two weeks"));
        Assert.Throws<ArgumentOutOfRangeException>(() => outgoing.PauseFor(new DateOnly(2026, 11, 1), 0, "Nothing"));
        Assert.True(outgoing.IsPaymentDate(new DateOnly(2026, 11, 1)));
        Assert.False(outgoing.IsPaymentDate(new DateOnly(2025, 11, 1)));
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
        Assert.Throws<InvalidOperationException>(() => outgoing.CorrectStatusPeriod(pause.Id, OutgoingStatusReason.Paused, new DateOnly(2026, 11, 2), null, "x", "y"));
    }

    [Theory]
    [InlineData("2026-11-01", 0)]
    [InlineData("2026-11-02", 30)]
    [InlineData("2026-11-16", 30)]
    public void Ending_counts_the_month_only_if_its_payment_is_before_the_end(string endsFrom, decimal november)
    {
        var outgoing = Create(30, PaymentSchedule.Monthly);
        outgoing.End(DateOnly.Parse(endsFrom), "Cancelled");

        Assert.Equal(november, Month(outgoing, 2026, 11));
        Assert.Equal(0m, Month(outgoing, 2026, 12));
        Assert.Equal(DateOnly.Parse(endsFrom), outgoing.EndsOn);
    }

    [Fact]
    public void Month_end_payment_days_are_clamped()
    {
        var outgoing = Create(100, PaymentSchedule.Monthly, new DateOnly(2026, 1, 31));
        outgoing.Pause(new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31), "February off");

        Assert.Equal(new[] { new DateOnly(2026, 2, 28) }, outgoing.CountingDaysIn(2026, 2));
        Assert.Equal(0m, Month(outgoing, 2026, 2));
        Assert.Equal(100m, Month(outgoing, 2026, 3));
    }

    // ---- Payment and renewal dates ----

    [Theory]
    [InlineData("2026-01-01", "2026-01-10")]
    [InlineData("2026-11-05", "2026-11-14")]
    [InlineData("2026-11-14", "2026-11-14")]
    [InlineData("2026-11-15", "2026-12-12")]
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
        paused.Pause(new DateOnly(2027, 1, 3), null, "Frozen");
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

    // ---- Scheduled payments (BUD-82, ADR-013) ----

    private static string[] Payments(Outgoing outgoing, DateOnly from, DateOnly to) =>
        outgoing.PaymentsBetween(from, to).Select(p => $"{p.Date:yyyy-MM-dd} {p.Amount}").ToArray();

    [Fact]
    public void Monthly_payments_on_the_31st_fall_on_the_last_day_of_short_months()
    {
        var rent = Create(800m, PaymentSchedule.Monthly, new DateOnly(2026, 1, 31));

        Assert.Equal(
            new[] { "2027-01-31 800", "2027-02-28 800", "2027-03-31 800" },
            Payments(rent, new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31)));
    }

    [Fact]
    public void A_yearly_bill_is_only_due_in_its_month_and_weekly_ones_every_week()
    {
        var insurance = Create(480m, PaymentSchedule.Yearly, new DateOnly(2026, 3, 14));
        var food = Create(120m, PaymentSchedule.Weekly, new DateOnly(2026, 11, 2));

        Assert.Empty(Payments(insurance, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)));
        Assert.Equal(new[] { "2027-03-14 480" }, Payments(insurance, new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31)));
        Assert.Equal(
            new[] { "2026-11-02 120", "2026-11-09 120", "2026-11-16 120", "2026-11-23 120", "2026-11-30 120" },
            Payments(food, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)));
    }

    [Fact]
    public void Payments_use_the_amount_in_force_and_skip_pauses_and_anything_after_the_end()
    {
        var gym = Create(30m, PaymentSchedule.Monthly, new DateOnly(2026, 6, 5));
        gym.ChangeAmount(new DateOnly(2026, 12, 5), 35m, PaymentSchedule.Monthly, "Price rise");
        gym.PauseFor(new DateOnly(2027, 1, 5), payments: 1, "Injured");
        gym.End(new DateOnly(2027, 4, 5), "Cancelled");

        Assert.Equal(
            new[] { "2026-11-05 30", "2026-12-05 35", "2027-02-05 35", "2027-03-05 35" },
            Payments(gym, new DateOnly(2026, 11, 1), new DateOnly(2027, 6, 30)));
    }

    [Fact]
    public void No_payments_are_listed_before_it_starts()
    {
        var car = Create(250m, PaymentSchedule.Monthly, new DateOnly(2027, 1, 15));

        Assert.Empty(Payments(car, new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31)));
        Assert.Equal(new[] { "2027-01-15 250" }, Payments(car, new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 31)));
    }
}
