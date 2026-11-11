using Budget.Shared.Income;

namespace Budget.Client.Tests;

/// <summary>Amounts never break across lines, least of all after the sign (BUD-128).</summary>
public sealed class MoneyTests
{
    [Theory]
    [InlineData(2307.69, "£2,307.69")]
    [InlineData(0, "£0.00")]
    [InlineData(-11.45, "−⁠£11.45")]
    public void Formats_with_a_joined_minus_sign(decimal amount, string expected) =>
        Assert.Equal(expected, Money.Format(amount));

    [Theory]
    [InlineData(11.45, true, "+⁠£11.45")]
    [InlineData(11.45, false, "−⁠£11.45")]
    [InlineData(-11.45, false, "−⁠£11.45")]
    public void Signs_money_coming_in_or_going_out(decimal amount, bool coming, string expected) =>
        Assert.Equal(expected, Money.Signed(amount, coming));
}
