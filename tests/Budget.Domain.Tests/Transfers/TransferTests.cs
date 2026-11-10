using Budget.Domain.Common;
using Budget.Domain.Households;
using Budget.Domain.Outgoings;
using Budget.Domain.Transfers;

namespace Budget.Domain.Tests.Transfers;

/// <summary>Transfers between accounts (BUD-91, ADR-014): the same timeline rules as outgoings.</summary>
public class TransferTests
{
    private static readonly Guid Household = Guid.NewGuid();
    private static readonly Guid Current = Guid.NewGuid();
    private static readonly Guid Joint = Guid.NewGuid();

    private static TransferDetails Details(DateOnly first, Guid? to = null) => new()
    {
        Name = "  Bills top-up ",
        FromAccountId = Current,
        ToAccountId = to ?? Joint,
        FirstPaymentDate = first,
    };

    private static Transfer Create(decimal amount, PaymentSchedule schedule, DateOnly? first = null) =>
        Transfer.Create(Household, Details(first ?? new DateOnly(2026, 1, 25)), amount, schedule, "Starting amount");

    [Fact]
    public void Moves_money_between_two_different_accounts()
    {
        var transfer = Create(400m, PaymentSchedule.Monthly);

        Assert.Equal(("Bills top-up", Current, Joint), (transfer.Name, transfer.FromAccountId, transfer.ToAccountId));
        Assert.Equal(new DateOnly(2026, 1, 25), transfer.StartsOn);
        Assert.Throws<ArgumentException>(() => Transfer.Create(Household, Details(new DateOnly(2026, 1, 1), to: Current), 10m, PaymentSchedule.Monthly, "Start"));
        Assert.Throws<ArgumentException>(() => Transfer.Create(Household, Details(new DateOnly(2026, 1, 1), to: Guid.Empty), 10m, PaymentSchedule.Monthly, "Start"));
    }

    [Fact]
    public void A_name_is_optional()
    {
        var transfer = Transfer.Create(Household, Details(new DateOnly(2026, 1, 1)) with { Name = " " }, 10m, PaymentSchedule.Monthly, "Start");

        Assert.Null(transfer.Name);
    }

    [Fact]
    public void Payments_follow_the_schedule_and_amount_changes()
    {
        var transfer = Create(400m, PaymentSchedule.Monthly);
        transfer.ChangeAmount(new DateOnly(2026, 4, 25), 450m, PaymentSchedule.Monthly, "Council tax went up");

        Assert.Equal(
            new[] { (new DateOnly(2026, 2, 25), 400m), (new DateOnly(2026, 3, 25), 400m), (new DateOnly(2026, 4, 25), 450m) },
            transfer.PaymentsBetween(new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 30)).Select(p => (p.Date, p.Amount)));
        Assert.Equal(450m, Values.RoundForDisplay(transfer.MonthlyEquivalentForMonth(2026, 5)));
    }

    [Fact]
    public void Can_be_paused_and_ended()
    {
        var transfer = Create(100m, PaymentSchedule.Monthly);

        var pause = transfer.PauseFor(new DateOnly(2026, 3, 25), 2, "Holiday");
        transfer.End(new DateOnly(2026, 8, 25), "Account closed");

        Assert.Equal(new DateOnly(2026, 5, 25), pause.ResumesOn);
        Assert.Equal(2, transfer.PaymentsSkippedBy(pause));
        Assert.Equal(
            new[] { new DateOnly(2026, 2, 25), new DateOnly(2026, 5, 25), new DateOnly(2026, 6, 25), new DateOnly(2026, 7, 25) },
            transfer.PaymentsBetween(new DateOnly(2026, 2, 1), new DateOnly(2026, 12, 31)).Select(p => p.Date));
        Assert.Equal(new DateOnly(2026, 8, 25), transfer.EndsOn);
        Assert.Throws<InvalidOperationException>(() => transfer.Pause(new DateOnly(2026, 6, 26), null, "Not a payment date"));
    }

    [Fact]
    public void Corrections_keep_the_original()
    {
        var transfer = Create(100m, PaymentSchedule.Monthly);
        var change = transfer.ChangeAmount(new DateOnly(2026, 6, 25), 120m, PaymentSchedule.Monthly, "Rise");

        transfer.CorrectAmountChange(change.Id, new DateOnly(2026, 6, 25), 125m, PaymentSchedule.Monthly, "Rise", "Typo");
        transfer.DeleteAmountChange(change.Id, "Didn't happen");

        Assert.Single(transfer.AmountChanges);
        Assert.Equal(
            new[] { (OutgoingCorrectionAction.Edited, "Typo"), (OutgoingCorrectionAction.Deleted, "Didn't happen") },
            transfer.Corrections.Select(c => (c.Action, c.Reason)));
        Assert.Throws<InvalidOperationException>(() => transfer.DeleteAmountChange(transfer.AmountChanges.Single().Id, "First"));
    }
}

public class HouseholdTransfersSettingTests
{
    [Fact]
    public void Accounts_and_transfers_is_off_until_turned_on()
    {
        var household = Household.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.False(household.TransfersEnabled);

        household.SetTransfersEnabled(true);
        Assert.True(household.TransfersEnabled);
    }
}
