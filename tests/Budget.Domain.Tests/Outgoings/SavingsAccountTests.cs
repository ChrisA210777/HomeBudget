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
        PaymentMethod = PaymentMethod.StandingOrder,
        ReferenceLabel = "Account number",
        ReferenceValue = "12345678",
        Website = "https://example.com",
        ContactPhone = "0800 123 456",
    };

    [Theory]
    [InlineData(OutgoingKind.Bill, true, true)]
    [InlineData(OutgoingKind.Savings, true, false)]
    [InlineData(OutgoingKind.Allowance, false, false)]
    public void Payment_details_are_kept_only_where_they_apply(OutgoingKind kind, bool method, bool rest)
    {
        var outgoing = Outgoing.Create(Guid.NewGuid(), Details(kind), 100m, PaymentSchedule.Monthly, "Start");

        Assert.Equal(method, outgoing.PaymentMethod is not null);
        Assert.Equal(new[] { rest, rest, rest, rest }, new[] { outgoing.ReferenceLabel, outgoing.ReferenceValue, outgoing.Website, outgoing.ContactPhone }.Select(v => v is not null));
    }

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
