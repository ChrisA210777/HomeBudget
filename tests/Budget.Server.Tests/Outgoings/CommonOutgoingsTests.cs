using Budget.Domain.Outgoings;
using Budget.Shared.Outgoings;

namespace Budget.Server.Tests.Outgoings;

/// <summary>The built-in list of common outgoings must only contain valid entries (BUD-54).</summary>
public class CommonOutgoingsTests
{
    public static TheoryData<string> Names() => [.. CommonOutgoings.All.Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Names))]
    public void Each_common_outgoing_is_valid(string name)
    {
        var common = CommonOutgoings.Find(name)!;

        Assert.Contains(common.Category, OutgoingCategory.DefaultNames);
        Assert.False(common.Kind == OutgoingKindDto.Allowance && common.Renews, "Allowances don't renew.");
        Assert.False(common.AutoRenews && !common.Renews, "Only something that renews can auto-renew.");
        Assert.False(common.AlertPeriod != AlertPeriodDto.None && !common.Renews, "Only something that renews has a reminder.");

        var request = new CreateOutgoingRequest
        {
            Kind = common.Kind,
            Name = common.Name,
            CategoryId = Guid.NewGuid(),
            Priority = common.Priority,
            Amount = 10m,
            Every = common.Every,
            Unit = common.Unit,
            FirstPaymentDate = new DateOnly(2026, 1, 1),
            PaymentMethod = common.PaymentMethod,
            ReferenceLabel = common.ReferenceLabel,
            Website = common.Website,
            RenewalDate = common.Renews ? new DateOnly(2027, 1, 1) : null,
            RenewalEveryMonths = common.RenewalEveryMonths,
            AutoRenews = common.AutoRenews,
            AlertPeriod = common.AlertPeriod,
        };
        Assert.Empty(OutgoingValidator.Validate(request));
    }

    [Fact]
    public void Names_are_unique_ignoring_case()
    {
        Assert.Equal(CommonOutgoings.All.Count, CommonOutgoings.All.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("Mortgage")]
    [InlineData("Council tax")]
    [InlineData("TV Licence")]
    [InlineData("Car insurance")]
    [InlineData("MOT")]
    [InlineData("Netflix")]
    [InlineData("NHS prescription prepayment certificate")]
    [InlineData("Boiler service")]
    [InlineData("National Trust membership")]
    [InlineData("Food shopping")]
    [InlineData("Replacement fund (depreciation)")]
    [InlineData("Discretionary spending")]
    [InlineData("Bank account fee")]
    public void The_list_covers_the_agreed_outgoings(string name)
    {
        Assert.NotNull(CommonOutgoings.Find(name));
    }
}
