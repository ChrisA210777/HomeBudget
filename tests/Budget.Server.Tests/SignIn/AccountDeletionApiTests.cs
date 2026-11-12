using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Domain.People;
using Budget.Server.Features.SignIn;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Account;
using Budget.Shared.Activity;
using Budget.Shared.Households;
using Budget.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.SignIn;

/// <summary>Deleting your own account (BUD-151).</summary>
[Collection(ApiCollection.Name)]
public class AccountDeletionApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Deleting_takes_you_out_of_shared_households_and_removes_your_own_and_your_name()
    {
        // Jane shares the Austins' household as an Admin, with a person linked to her, and has her own household.
        var (ownerId, sharedId) = await CreateHouseholdOwnerAsync("Chris Austin");
        var (janeId, janesOwnId) = await CreateHouseholdOwnerAsync("Jane Austin");
        var personId = await JoinAsync(sharedId, janeId, "Jane");
        using var jane = CreateClient(signedInAs: janeId);
        using var owner = CreateClient(signedInAs: ownerId);

        // She adds a person (recorded in recent activity) and invites someone (still pending).
        using (var added = await jane.PostAsJsonAsync(Url(PeopleRoutes.People(sharedId)), new SavePersonRequest { DisplayName = "Sam" }))
        using (var invited = await jane.PostAsJsonAsync(Url(InvitationRoutes.Invitations(sharedId)), new CreateInvitationRequest { Email = "sam@example.com", Role = "Viewer" }))
        {
            Assert.True(added.IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        }

        var check = (await jane.GetFromJsonAsync<AccountDeletionCheck>(Url(AccountRoutes.AccountDeletion)))!;
        Assert.True(check.CanDelete);
        Assert.Equal(janesOwnId, Assert.Single(check.HouseholdsToDelete).HouseholdId);
        Assert.Equal(1, check.HouseholdsToLeave);

        using var deleted = await jane.PostAsJsonAsync(Url(AccountRoutes.DeleteAccount), new DeleteAccountRequest { Confirmation = "delete" });

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.Users.AnyAsync(u => u.Id == janeId));
            Assert.False(await db.UserLogins.AnyAsync(l => l.UserId == janeId));
            Assert.False(await db.Households.AnyAsync(h => h.Id == janesOwnId));
            Assert.False(await db.HouseholdMembers.AnyAsync(m => m.UserId == janeId));
            Assert.False(await db.Invitations.AnyAsync(i => i.HouseholdId == sharedId && i.AcceptedAt == null));

            // The shared household keeps her person and what she added, without the link to her.
            var person = await db.People.IgnoreQueryFilters().SingleAsync(p => p.Id == personId);
            Assert.Null(person.LinkedUserId);
            Assert.True(await db.People.IgnoreQueryFilters().AnyAsync(p => p.HouseholdId == sharedId && p.DisplayName == "Sam"));
            Assert.False(await db.ActivityEntries.IgnoreQueryFilters().AnyAsync(a => a.UserId == janeId));
        });

        var activity = (await owner.GetFromJsonAsync<ActivityResponse>(Url(ActivityRoutes.Activity(sharedId, take: 10))))!;
        Assert.Contains(activity.Items, a => a.Who == AccountDeletionEndpoints.FormerMember && a.Summary == "left the household");
        Assert.Contains(activity.Items, a => a.Who == AccountDeletionEndpoints.FormerMember && a.Summary.Contains("Sam", StringComparison.Ordinal));
        Assert.DoesNotContain(activity.Items, a => a.Who.Contains("Jane", StringComparison.Ordinal));

        // She's signed out: her next request finds no user.
        using var after = await jane.GetAsync(Url(AccountRoutes.CurrentUser));
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task You_cant_delete_while_you_own_a_household_others_use()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        await AddMemberAsync(householdId, HouseholdRole.Viewer);
        using var owner = CreateClient(signedInAs: ownerId);

        var check = (await owner.GetFromJsonAsync<AccountDeletionCheck>(Url(AccountRoutes.AccountDeletion)))!;
        using var refused = await owner.PostAsJsonAsync(Url(AccountRoutes.DeleteAccount), new DeleteAccountRequest { Confirmation = "DELETE" });

        Assert.False(check.CanDelete);
        Assert.Equal((householdId, 1), (Assert.Single(check.HouseholdsToHandOn).HouseholdId, check.HouseholdsToHandOn[0].OtherMembers));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        await ExecuteDbContextAsync(async db =>
        {
            Assert.True(await db.Users.AnyAsync(u => u.Id == ownerId));
            Assert.True(await db.Households.AnyAsync(h => h.Id == householdId));
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yes")]
    public async Task Deleting_needs_DELETE_typed(string? confirmation)
    {
        var (userId, _) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: userId);

        using var response = await client.PostAsJsonAsync(Url(AccountRoutes.DeleteAccount), new DeleteAccountRequest { Confirmation = confirmation });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ExecuteDbContextAsync(async db => Assert.True(await db.Users.AnyAsync(u => u.Id == userId)));
    }

    [Fact]
    public async Task You_must_be_signed_in()
    {
        using var client = CreateClient();

        using var check = await client.GetAsync(Url(AccountRoutes.AccountDeletion));
        using var delete = await client.PostAsJsonAsync(Url(AccountRoutes.DeleteAccount), new DeleteAccountRequest { Confirmation = "DELETE" });

        Assert.Equal(HttpStatusCode.Unauthorized, check.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    /// <summary>Adds the user to the household as an Admin, with a person linked to them.</summary>
    private async Task<Guid> JoinAsync(Guid householdId, Guid userId, string personName)
    {
        var person = Person.Create(householdId, personName, userId);
        await ExecuteDbContextAsync(async db =>
        {
            var household = await db.Households.Include(h => h.Members).SingleAsync(h => h.Id == householdId);
            household.AddMember(userId, HouseholdRole.Admin, DateTimeOffset.UtcNow);
            db.People.Add(person);
            await db.SaveChangesAsync();
        });

        return person.Id;
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
