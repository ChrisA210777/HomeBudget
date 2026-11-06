using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Domain.People;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.Account;
using Budget.Shared.Households;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.Households;

/// <summary>Changing roles (BUD-27), removing members (BUD-28) and leaving (BUD-29).</summary>
[Collection(ApiCollection.Name)]
public class MemberManagementApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task An_admin_can_change_a_members_role_and_it_applies_to_their_next_request()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var memberId = await AddMemberAsync(householdId, HouseholdRole.Viewer);
        using var owner = CreateClient(signedInAs: ownerId);
        using var member = CreateClient(signedInAs: memberId);

        using var response = await owner.PutAsJsonAsync(Url(MemberRoutes.Role(householdId, memberId)), new ChangeRoleRequest { Role = "User" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var household = await member.GetFromJsonAsync<HouseholdResponse>(Url(HouseholdRoutes.Household(householdId)));
        Assert.Equal("User", household!.Role);
    }

    [Fact]
    public async Task The_owners_role_cant_be_changed()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var adminId = await AddMemberAsync(householdId, HouseholdRole.Admin);
        using var admin = CreateClient(signedInAs: adminId);

        using var response = await admin.PutAsJsonAsync(Url(MemberRoutes.Role(householdId, ownerId)), new ChangeRoleRequest { Role = "Viewer" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("owner", problem!.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("admin")]
    [InlineData("")]
    public async Task Only_Viewer_User_or_Admin_are_accepted(string role)
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var memberId = await AddMemberAsync(householdId, HouseholdRole.Viewer);
        using var owner = CreateClient(signedInAs: ownerId);

        using var response = await owner.PutAsJsonAsync(Url(MemberRoutes.Role(householdId, memberId)), new ChangeRoleRequest { Role = role });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(nameof(ChangeRoleRequest.Role), problem!.Errors.Keys);
    }

    [Fact]
    public async Task Changing_or_removing_someone_who_isnt_a_member_is_404()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var (outsiderId, _) = await CreateHouseholdOwnerAsync("Outsider");
        using var owner = CreateClient(signedInAs: ownerId);

        using var role = await owner.PutAsJsonAsync(Url(MemberRoutes.Role(householdId, outsiderId)), new ChangeRoleRequest { Role = "User" });
        using var remove = await owner.DeleteAsync(Url(MemberRoutes.Member(householdId, outsiderId)));

        Assert.Equal(HttpStatusCode.NotFound, role.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
    }

    [Fact]
    public async Task Users_and_viewers_cant_change_roles_or_remove_members()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var targetId = await AddMemberAsync(householdId, HouseholdRole.Viewer, "Target");

        foreach (var callerRole in new[] { HouseholdRole.Viewer, HouseholdRole.User })
        {
            var callerId = await AddMemberAsync(householdId, callerRole, $"{callerRole} caller");
            using var caller = CreateClient(signedInAs: callerId);

            using var role = await caller.PutAsJsonAsync(Url(MemberRoutes.Role(householdId, targetId)), new ChangeRoleRequest { Role = "Admin" });
            using var remove = await caller.DeleteAsync(Url(MemberRoutes.Member(householdId, targetId)));

            Assert.Equal(HttpStatusCode.Forbidden, role.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
        }

        Assert.NotEqual(Guid.Empty, ownerId);
    }

    [Fact]
    public Task Other_households_cant_change_roles() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Put, h => MemberRoutes.Role(h, Guid.NewGuid()), new ChangeRoleRequest { Role = "User" });

    [Fact]
    public Task Other_households_cant_remove_members() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Delete, h => MemberRoutes.Member(h, Guid.NewGuid()));

    [Fact]
    public async Task A_removed_member_loses_access_and_their_person_stays_unlinked()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var memberId = await AddMemberAsync(householdId, HouseholdRole.User);
        var personId = await AddPersonAsync(householdId, "Partner", memberId);
        await SetActiveHouseholdAsync(memberId, householdId);
        using var owner = CreateClient(signedInAs: ownerId);
        using var member = CreateClient(signedInAs: memberId);

        using var response = await owner.DeleteAsync(Url(MemberRoutes.Member(householdId, memberId)));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var afterwards = await member.GetAsync(Url(HouseholdRoutes.Household(householdId)));
        Assert.Equal(HttpStatusCode.NotFound, afterwards.StatusCode);
        await ExecuteDbContextAsync(async db =>
        {
            var person = await db.People.IgnoreQueryFilters().SingleAsync(p => p.Id == personId);
            Assert.Null(person.LinkedUserId);
            Assert.Equal("Partner", person.DisplayName);
            var user = await db.Users.SingleAsync(u => u.Id == memberId);
            Assert.Null(user.LastActiveHouseholdId);
        });
    }

    [Fact]
    public async Task The_owner_cant_be_removed()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var adminId = await AddMemberAsync(householdId, HouseholdRole.Admin);
        using var admin = CreateClient(signedInAs: adminId);

        using var response = await admin.DeleteAsync(Url(MemberRoutes.Member(householdId, ownerId)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_member_can_leave_and_is_moved_to_their_own_household()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Owner");
        var (memberId, ownHouseholdId) = await CreateHouseholdOwnerAsync("Member");
        await ExecuteDbContextAsync(async db =>
        {
            var household = await db.Households.Include(h => h.Members).SingleAsync(h => h.Id == householdId);
            household.AddMember(memberId, HouseholdRole.Admin, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        });
        await SetActiveHouseholdAsync(memberId, householdId);
        using var member = CreateClient(signedInAs: memberId);

        using var response = await member.DeleteAsync(Url(MemberRoutes.Membership(householdId)));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var me = await member.GetFromJsonAsync<CurrentUserResponse>(Url(Budget.Shared.AccountRoutes.CurrentUser));
        Assert.Equal(ownHouseholdId, me!.LastActiveHouseholdId);
        Assert.DoesNotContain(me.Households, h => h.HouseholdId == householdId);
        Assert.NotEqual(Guid.Empty, ownerId);
    }

    [Fact]
    public async Task The_owner_cant_leave()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var owner = CreateClient(signedInAs: ownerId);

        using var response = await owner.DeleteAsync(Url(MemberRoutes.Membership(householdId)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("delete the household", problem!.Title, StringComparison.Ordinal);
    }

    [Fact]
    public Task Every_non_owner_role_can_leave() =>
        AssertMinimumRoleAsync(HttpMethod.Delete, MemberRoutes.Membership, HouseholdRole.Viewer);

    [Fact]
    public Task Other_households_cant_be_left() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Delete, MemberRoutes.Membership);

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private async Task<Guid> AddPersonAsync(Guid householdId, string name, Guid linkedUserId)
    {
        var person = Person.Create(householdId, name, linkedUserId);
        await ExecuteDbContextAsync(async db =>
        {
            db.People.Add(person);
            await db.SaveChangesAsync();
        });
        return person.Id;
    }

    private Task SetActiveHouseholdAsync(Guid userId, Guid householdId) =>
        ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.LastActiveHouseholdId = householdId;
            await db.SaveChangesAsync();
        });
}
