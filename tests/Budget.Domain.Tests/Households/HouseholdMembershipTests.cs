using Budget.Domain.Households;

namespace Budget.Domain.Tests.Households;

/// <summary>Changing roles and removing members (BUD-27, BUD-28, BUD-29).</summary>
public class HouseholdMembershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 15, 0, 0, TimeSpan.Zero);
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Household _household;

    public HouseholdMembershipTests() => _household = Household.Create(_ownerId, Now);

    [Theory]
    [InlineData(HouseholdRole.Viewer, HouseholdRole.Admin)]
    [InlineData(HouseholdRole.Admin, HouseholdRole.User)]
    [InlineData(HouseholdRole.User, HouseholdRole.Viewer)]
    public void A_members_role_can_be_changed(HouseholdRole from, HouseholdRole to)
    {
        var userId = _household.AddMember(Guid.NewGuid(), from, Now).UserId;

        _household.ChangeRole(userId, to);

        Assert.Equal(to, Role(userId));
    }

    [Fact]
    public void The_owners_role_cant_be_changed()
    {
        var ex = Assert.Throws<HouseholdMembershipException>(() => _household.ChangeRole(_ownerId, HouseholdRole.User));

        Assert.Contains("owner", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HouseholdRole.Admin, Role(_ownerId));
    }

    [Fact]
    public void Setting_the_same_role_again_is_allowed_even_for_the_owner()
    {
        _household.ChangeRole(_ownerId, HouseholdRole.Admin);

        Assert.Equal(HouseholdRole.Admin, Role(_ownerId));
    }

    [Fact]
    public void Any_non_owner_member_can_be_removed()
    {
        var adminId = _household.AddMember(Guid.NewGuid(), HouseholdRole.Admin, Now).UserId;
        var viewerId = _household.AddMember(Guid.NewGuid(), HouseholdRole.Viewer, Now).UserId;

        _household.RemoveMember(adminId);
        _household.RemoveMember(viewerId);

        Assert.Equal(_ownerId, Assert.Single(_household.Members).UserId);
    }

    [Fact]
    public void The_owner_cant_be_removed_or_leave()
    {
        var ex = Assert.Throws<HouseholdMembershipException>(() => _household.RemoveMember(_ownerId));

        Assert.Contains("delete the household", ex.Message, StringComparison.Ordinal);
        Assert.Single(_household.Members);
    }

    [Fact]
    public void Unknown_members_are_reported()
    {
        Assert.Throws<KeyNotFoundException>(() => _household.RemoveMember(Guid.NewGuid()));
        Assert.Throws<KeyNotFoundException>(() => _household.ChangeRole(Guid.NewGuid(), HouseholdRole.User));
    }

    [Fact]
    public void Ownership_can_be_handed_to_another_Admin_and_the_old_owner_stays_an_Admin()
    {
        var nextId = _household.AddMember(Guid.NewGuid(), HouseholdRole.Admin, Now).UserId;

        _household.TransferOwnership(nextId);

        Assert.True(_household.Members.Single(m => m.UserId == nextId).IsOwner);
        Assert.False(_household.Members.Single(m => m.UserId == _ownerId).IsOwner);
        Assert.Equal(HouseholdRole.Admin, Role(_ownerId));
        Assert.Single(_household.Members, m => m.IsOwner);
    }

    [Theory]
    [InlineData(HouseholdRole.User)]
    [InlineData(HouseholdRole.Viewer)]
    public void Only_an_Admin_can_become_the_owner(HouseholdRole role)
    {
        var memberId = _household.AddMember(Guid.NewGuid(), role, Now).UserId;

        var ex = Assert.Throws<HouseholdMembershipException>(() => _household.TransferOwnership(memberId));

        Assert.Contains("Admin", ex.Message, StringComparison.Ordinal);
        Assert.True(_household.Members.Single(m => m.UserId == _ownerId).IsOwner);
    }

    [Fact]
    public void Once_ownership_is_handed_on_the_old_owner_can_leave()
    {
        var nextId = _household.AddMember(Guid.NewGuid(), HouseholdRole.Admin, Now).UserId;
        _household.TransferOwnership(nextId);

        _household.RemoveMember(_ownerId);

        Assert.DoesNotContain(_household.Members, m => m.UserId == _ownerId);
    }

    private HouseholdRole Role(Guid userId) => _household.Members.Single(m => m.UserId == userId).Role;
}
