using Budget.Domain.Outgoings;

namespace Budget.Domain.Tests.Outgoings;

/// <summary>Savings go into an account or pot, not to someone (BUD-100).</summary>
public class SavingsAccountTests
{
    private static readonly Guid Isa = Guid.NewGuid();

    private static OutgoingDetails Details(OutgoingKind kind) => new()
    {
        Name = "Deposit",
        Kind = kind,
        CategoryId = Guid.NewGuid(),
        FirstPaymentDate = new DateOnly(2026, 1, 1),
        Recipient = "Someone",
        SavingsAccountId = Isa,
    };

    [Fact]
    public void Savings_keep_the_account_they_go_into_and_no_recipient()
    {
        var savings = Outgoing.Create(Guid.NewGuid(), Details(OutgoingKind.Savings), 100m, PaymentSchedule.Monthly, "Start");

        Assert.Equal(((Guid?)Isa, (string?)null), (savings.SavingsAccountId, savings.Recipient));
    }

    [Fact]
    public void A_bill_is_paid_to_someone_and_into_no_account_of_ours()
    {
        var bill = Outgoing.Create(Guid.NewGuid(), Details(OutgoingKind.Bill), 100m, PaymentSchedule.Monthly, "Start");

        Assert.Equal(((Guid?)null, (string?)"Someone"), (bill.SavingsAccountId, bill.Recipient));
    }
}
