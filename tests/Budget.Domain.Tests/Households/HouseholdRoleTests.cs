using Budget.Domain.Households;

namespace Budget.Domain.Tests.Households;

public class HouseholdRoleTests
{
    [Theory]
    [InlineData(HouseholdRole.Viewer, HouseholdRole.Viewer, true)]
    [InlineData(HouseholdRole.Viewer, HouseholdRole.User, false)]
    [InlineData(HouseholdRole.Viewer, HouseholdRole.Admin, false)]
    [InlineData(HouseholdRole.User, HouseholdRole.Viewer, true)]
    [InlineData(HouseholdRole.User, HouseholdRole.User, true)]
    [InlineData(HouseholdRole.User, HouseholdRole.Admin, false)]
    [InlineData(HouseholdRole.Admin, HouseholdRole.Viewer, true)]
    [InlineData(HouseholdRole.Admin, HouseholdRole.User, true)]
    [InlineData(HouseholdRole.Admin, HouseholdRole.Admin, true)]
    public void Satisfies_compares_role_against_required_role(HouseholdRole role, HouseholdRole required, bool expected)
    {
        Assert.Equal(expected, role.Satisfies(required));
    }

    [Fact]
    public void Satisfies_rejects_unknown_roles()
    {
        const HouseholdRole unknown = (HouseholdRole)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => unknown.Satisfies(HouseholdRole.Viewer));
        Assert.Throws<ArgumentOutOfRangeException>(() => HouseholdRole.Admin.Satisfies(unknown));
    }
}
