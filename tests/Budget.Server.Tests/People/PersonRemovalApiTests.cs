using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Accounts;
using Budget.Domain.Households;
using Budget.Domain.Income;
using Budget.Domain.Outgoings;
using Budget.Domain.People;
using Budget.Server.Features.People;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.Activity;
using Budget.Shared.Households;
using Budget.Shared.People;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.People;

/// <summary>Admins remove a person, moving or deleting their items (BUD-112, ADR-016).</summary>
[Collection(ApiCollection.Name)]
public class PersonRemovalApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Removing_moves_or_deletes_their_items_as_chosen_and_keeps_no_name()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        var (sam, chris, accountId) = await SetUpSamAsync(householdId);
        using var owner = CreateClient(signedInAs: ownerId);
        using (var invited = await owner.PostAsJsonAsync(Url(InvitationRoutes.Invitations(householdId)), new CreateInvitationRequest { Email = "sam@example.com", PersonId = sam }))
        {
            Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        }

        var check = (await owner.GetFromJsonAsync<PersonRemovalCheck>(Url(PeopleRoutes.Removal(householdId, sam))))!;
        Assert.Equal(new PersonRemovalCheck(IncomeCount: 1, OutgoingCount: 2, AccountCount: 1, LinkedToMember: false), check);

        using var removed = await owner.PostAsJsonAsync(Url(PeopleRoutes.Removal(householdId, sam)), new RemovePersonRequest
        {
            Income = RemovalChoices.Person,
            IncomeTo = chris,
            Outgoings = RemovalChoices.Delete,
            ArchiveAccounts = true,
        });

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        await ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.People.IgnoreQueryFilters().AnyAsync(p => p.Id == sam));
            Assert.Equal(chris, (await db.IncomeStreams.IgnoreQueryFilters().SingleAsync(s => s.HouseholdId == householdId && s.Name == "Paper round")).PersonId);
            Assert.False(await db.Outgoings.IgnoreQueryFilters().AnyAsync(o => o.HouseholdId == householdId && (o.Name == "Phone" || o.Name == "Gym")));
            var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(a => a.Id == accountId);
            Assert.Equal((null, true), (account.OwnerPersonId, account.IsArchived));
            Assert.False(await db.Invitations.AnyAsync(i => i.HouseholdId == householdId && i.AcceptedAt == null && i.RevokedAt == null));
        });

        var activity = (await owner.GetFromJsonAsync<ActivityResponse>(Url(ActivityRoutes.Activity(householdId, take: 10))))!;
        Assert.Contains(activity.Items, a => a.Summary == "removed a person from the household's people");
    }

    [Fact]
    public async Task Outgoings_can_become_the_households_and_accounts_stay_open()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        var (sam, chris, accountId) = await SetUpSamAsync(householdId);
        using var owner = CreateClient(signedInAs: ownerId);

        using var removed = await owner.PostAsJsonAsync(Url(PeopleRoutes.Removal(householdId, sam)), new RemovePersonRequest
        {
            Income = RemovalChoices.Delete,
            Outgoings = RemovalChoices.Household,
        });

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        await ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.IncomeStreams.IgnoreQueryFilters().AnyAsync(s => s.HouseholdId == householdId && s.Name == "Paper round"));
            var outgoings = await db.Outgoings.IgnoreQueryFilters().Where(o => o.HouseholdId == householdId && (o.Name == "Phone" || o.Name == "Gym")).ToListAsync();
            Assert.Equal(2, outgoings.Count);
            Assert.All(outgoings, o => Assert.Null(o.PersonId));
            var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(a => a.Id == accountId);
            Assert.Equal((null, false), (account.OwnerPersonId, account.IsArchived));
        });
    }

    [Fact]
    public async Task A_choice_is_needed_for_each_kind_of_item_they_have()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var (sam, _, _) = await SetUpSamAsync(householdId);
        using var owner = CreateClient(signedInAs: ownerId);

        using var refused = await owner.PostAsJsonAsync(Url(PeopleRoutes.Removal(householdId, sam)), new RemovePersonRequest
        {
            Income = RemovalChoices.Person,
            IncomeTo = sam,
        });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal(new[] { nameof(RemovePersonRequest.Income), nameof(RemovePersonRequest.Outgoings) }, problem!.Errors.Keys.Order());
        await ExecuteDbContextAsync(async db => Assert.True(await db.People.IgnoreQueryFilters().AnyAsync(p => p.Id == sam)));
    }

    [Fact]
    public async Task Someone_who_signs_in_cant_be_removed()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var owner = CreateClient(signedInAs: ownerId);
        var ownPerson = Guid.Empty;
        await ExecuteDbContextAsync(async db =>
            ownPerson = await db.People.IgnoreQueryFilters().Where(p => p.HouseholdId == householdId && p.LinkedUserId == ownerId).Select(p => p.Id).SingleAsync());

        var check = (await owner.GetFromJsonAsync<PersonRemovalCheck>(Url(PeopleRoutes.Removal(householdId, ownPerson))))!;
        using var refused = await owner.PostAsJsonAsync(Url(PeopleRoutes.Removal(householdId, ownPerson)), new RemovePersonRequest());

        Assert.True(check.LinkedToMember);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(PersonRemoval.LinkedToMember, problem!.Title);
    }

    [Fact]
    public async Task Only_Admins_can_remove_people()
    {
        var (_, householdId) = await CreateHouseholdOwnerAsync();
        var (sam, _, _) = await SetUpSamAsync(householdId);

        // Sam belongs to this household, so the shared role check (which makes its own household) can't be used.
        foreach (var role in new[] { HouseholdRole.Viewer, HouseholdRole.User })
        {
            using var member = CreateClient(signedInAs: await AddMemberAsync(householdId, role, $"{role} member"));
            using var check = await member.GetAsync(Url(PeopleRoutes.Removal(householdId, sam)));
            using var remove = await member.PostAsJsonAsync(Url(PeopleRoutes.Removal(householdId, sam)), new RemovePersonRequest { Income = RemovalChoices.Delete, Outgoings = RemovalChoices.Delete });
            Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (check.StatusCode, remove.StatusCode));
        }

        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, h => PeopleRoutes.Removal(h, sam));
        await ExecuteDbContextAsync(async db => Assert.True(await db.People.IgnoreQueryFilters().AnyAsync(p => p.Id == sam)));
    }

    /// <summary>Sam, who doesn't sign in, has a paper round, a phone bill and a gym membership, and a savings account.</summary>
    private async Task<(Guid Sam, Guid Chris, Guid AccountId)> SetUpSamAsync(Guid householdId)
    {
        var sam = Person.Create(householdId, "Sam");
        var account = Account.Create(householdId, "Sam's savings", sam.Id);
        var chris = Guid.Empty;
        await ExecuteDbContextAsync(async db =>
        {
            chris = await db.People.IgnoreQueryFilters().Where(p => p.HouseholdId == householdId).Select(p => p.Id).FirstAsync();
            var category = await db.OutgoingCategories.IgnoreQueryFilters().Where(c => c.HouseholdId == householdId).Select(c => c.Id).FirstAsync();
            db.People.Add(sam);
            db.Accounts.Add(account);
            db.IncomeStreams.Add(IncomeStream.Create(householdId, sam.Id, "Paper round", 120m, new DateOnly(2026, 1, 3), "Start", PaymentSchedule.Monthly));

            Outgoing Bill(string name) => Outgoing.Create(
                householdId, new OutgoingDetails { Name = name, CategoryId = category, FirstPaymentDate = new DateOnly(2026, 1, 1), PersonId = sam.Id },
                20m, PaymentSchedule.Monthly, "Start");
            db.Outgoings.AddRange(Bill("Phone"), Bill("Gym"));
            await db.SaveChangesAsync();
        });

        return (sam.Id, chris, account.Id);
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
