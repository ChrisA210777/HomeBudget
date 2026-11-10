using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.Accounts;
using Budget.Shared.Outgoings;
using Budget.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.Accounts;

/// <summary>/api/households/{id}/accounts (BUD-89, ADR-014).</summary>
[Collection(ApiCollection.Name)]
public class AccountApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Accounts_and_pots_are_listed_by_name_with_pots_under_their_account()
    {
        var (client, householdId) = await OwnerAsync();
        var monzo = await AddAsync(client, householdId, new SaveAccountRequest { Name = "Joint Monzo", Provider = "Monzo" });
        await AddAsync(client, householdId, new SaveAccountRequest { Name = "Holiday", ParentAccountId = monzo.Id });
        await AddAsync(client, householdId, new SaveAccountRequest { Name = "Food", ParentAccountId = monzo.Id });
        await AddAsync(client, householdId, new SaveAccountRequest { Name = "Chris current" });

        var accounts = await ListAsync(client, householdId);

        Assert.Equal(new[] { "Chris current", "Joint Monzo", "Joint Monzo › Food", "Joint Monzo › Holiday" }, accounts.Select(a => a.DisplayName));
        Assert.Equal(("Monzo", (Guid?)null), (accounts[1].Provider, accounts[1].OwnerPersonId));
        Assert.Equal(monzo.Id, accounts[2].ParentAccountId);
    }

    [Fact]
    public async Task An_account_can_belong_to_a_person_and_its_pots_follow()
    {
        var (client, householdId) = await OwnerAsync();
        var chris = (await client.GetFromJsonAsync<List<PersonSummary>>(Url(PeopleRoutes.People(householdId))))!.Single();
        var current = await AddAsync(client, householdId, new SaveAccountRequest { Name = "Current" });
        var savings = await AddAsync(client, householdId, new SaveAccountRequest { Name = "Savings", ParentAccountId = current.Id });

        using var response = await client.PutAsJsonAsync(Url(HouseholdAccountRoutes.Account(householdId, current.Id)),
            new SaveAccountRequest { Name = "Chris current", OwnerPersonId = chris.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = await ListAsync(client, householdId);
        Assert.Equal(("Chris current", (Guid?)chris.Id, chris.DisplayName), (accounts[0].Name, accounts[0].OwnerPersonId, accounts[0].OwnerName));
        Assert.Equal((Guid?)chris.Id, accounts.Single(a => a.Id == savings.Id).OwnerPersonId);
    }

    [Theory]
    [InlineData("", "Enter a name.")]
    [InlineData(" joint MONZO ", "There's already an account with that name.")]
    [InlineData("A › B", "A name can't contain ›, which shows a pot.")]
    public async Task Bad_names_are_refused(string name, string message)
    {
        var (client, householdId) = await OwnerAsync();
        await AddAsync(client, householdId, new SaveAccountRequest { Name = "Joint Monzo" });

        using var response = await client.PostAsJsonAsync(Url(HouseholdAccountRoutes.Accounts(householdId)), new SaveAccountRequest { Name = name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pots_cant_have_pots_and_owners_must_be_in_the_household()
    {
        var (client, householdId) = await OwnerAsync();
        var monzo = await AddAsync(client, householdId, new SaveAccountRequest { Name = "Joint Monzo" });
        var holiday = await AddAsync(client, householdId, new SaveAccountRequest { Name = "Holiday", ParentAccountId = monzo.Id });

        using var potInPot = await client.PostAsJsonAsync(Url(HouseholdAccountRoutes.Accounts(householdId)), new SaveAccountRequest { Name = "Spain", ParentAccountId = holiday.Id });
        using var stranger = await client.PostAsJsonAsync(Url(HouseholdAccountRoutes.Accounts(householdId)), new SaveAccountRequest { Name = "Theirs", OwnerPersonId = Guid.NewGuid() });

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (potInPot.StatusCode, stranger.StatusCode));
    }

    [Fact]
    public async Task Archived_accounts_arent_suggested_until_restored()
    {
        var (client, householdId) = await OwnerAsync();
        var old = await AddAsync(client, householdId, new SaveAccountRequest { Name = "Old bank" });
        await AddAsync(client, householdId, new SaveAccountRequest { Name = "New bank" });

        (await client.PostAsync(Url(HouseholdAccountRoutes.Archive(householdId, old.Id)), null)).Dispose();
        Assert.Equal(new[] { "New bank" }, await client.GetFromJsonAsync<List<string>>(Url(OutgoingRoutes.PaidFromNames(householdId))));
        Assert.True((await ListAsync(client, householdId)).Single(a => a.Id == old.Id).IsArchived);

        (await client.PostAsync(Url(HouseholdAccountRoutes.Restore(householdId, old.Id)), null)).Dispose();
        Assert.Equal(new[] { "New bank", "Old bank" }, await client.GetFromJsonAsync<List<string>>(Url(OutgoingRoutes.PaidFromNames(householdId))));
    }

    [Fact]
    public async Task Merging_moves_outgoings_and_pots_and_archives_the_old_account()
    {
        var (client, householdId) = await OwnerAsync();
        var category = await CategoryIdAsync(householdId);
        foreach (var (name, paidFrom) in new[] { ("Rent", "Joint"), ("Holiday", "Joint (Holiday Pot)"), ("Gym", "Joint account"), ("Spain", "Joint account › Holiday"), ("Car", "Joint (Car Pot)") })
        {
            using var created = await client.PostAsJsonAsync(Url(OutgoingRoutes.Outgoings(householdId)), Outgoing(category, name, paidFrom));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var before = await ListAsync(client, householdId);
        var joint = before.Single(a => a.DisplayName == "Joint");
        var jointAccount = before.Single(a => a.DisplayName == "Joint account");

        using var response = await client.PostAsJsonAsync(Url(HouseholdAccountRoutes.Merge(householdId, joint.Id)), new MergeAccountRequest { IntoAccountId = jointAccount.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await ListAsync(client, householdId);
        // Same-named pots combine; others move across.
        Assert.Equal(new[] { "Joint account", "Joint account › Car", "Joint account › Holiday" }, after.Where(a => !a.IsArchived).Select(a => a.DisplayName));
        Assert.Equal((2, 1, 2), (after.Single(a => a.DisplayName == "Joint account").OutgoingCount, after.Single(a => a.DisplayName == "Joint account › Car").OutgoingCount,
            after.Single(a => a.DisplayName == "Joint account › Holiday").OutgoingCount));
        Assert.True(after.Single(a => a.Id == joint.Id).IsArchived);
    }

    [Fact]
    public async Task An_account_with_pots_cant_be_merged_into_a_pot()
    {
        var (client, householdId) = await OwnerAsync();
        var monzo = await AddAsync(client, householdId, new SaveAccountRequest { Name = "Monzo" });
        await AddAsync(client, householdId, new SaveAccountRequest { Name = "Holiday", ParentAccountId = monzo.Id });
        var starling = await AddAsync(client, householdId, new SaveAccountRequest { Name = "Starling" });
        var food = await AddAsync(client, householdId, new SaveAccountRequest { Name = "Food", ParentAccountId = starling.Id });

        using var response = await client.PostAsJsonAsync(Url(HouseholdAccountRoutes.Merge(householdId, monzo.Id)), new MergeAccountRequest { IntoAccountId = food.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Anyone_can_see_accounts_users_can_change_them_and_only_admins_merge()
    {
        await AssertMinimumRoleAsync(HttpMethod.Get, HouseholdAccountRoutes.Accounts, HouseholdRole.Viewer);
        await AssertMinimumRoleAsync(HttpMethod.Post, HouseholdAccountRoutes.Accounts, HouseholdRole.User, () => new SaveAccountRequest { Name = $"Account {Guid.NewGuid():N}" });
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, HouseholdAccountRoutes.Accounts);
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Post, HouseholdAccountRoutes.Accounts, new SaveAccountRequest { Name = "Theirs" });

        // Merging is for Admins only.
        var (owner, householdId) = await OwnerAsync();
        var from = await AddAsync(owner, householdId, new SaveAccountRequest { Name = "From" });
        var into = await AddAsync(owner, householdId, new SaveAccountRequest { Name = "Into" });
        using var user = CreateClient(signedInAs: await AddMemberAsync(householdId, HouseholdRole.User));
        using var refused = await user.PostAsJsonAsync(Url(HouseholdAccountRoutes.Merge(householdId, from.Id)), new MergeAccountRequest { IntoAccountId = into.Id });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    private async Task<(HttpClient Client, Guid HouseholdId)> OwnerAsync()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        return (CreateClient(signedInAs: ownerId), householdId);
    }

    private static async Task<AccountSummary> AddAsync(HttpClient client, Guid householdId, SaveAccountRequest request)
    {
        using var response = await client.PostAsJsonAsync(Url(HouseholdAccountRoutes.Accounts(householdId)), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountSummary>())!;
    }

    private static async Task<List<AccountSummary>> ListAsync(HttpClient client, Guid householdId) =>
        (await client.GetFromJsonAsync<List<AccountSummary>>(Url(HouseholdAccountRoutes.Accounts(householdId))))!;

    private async Task<Guid> CategoryIdAsync(Guid householdId)
    {
        Guid id = default;
        await ExecuteDbContextAsync(async db => id = await db.OutgoingCategories.IgnoreQueryFilters()
            .Where(c => c.HouseholdId == householdId && c.Name == "Housing").Select(c => c.Id).SingleAsync());
        return id;
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static CreateOutgoingRequest Outgoing(Guid categoryId, string name, string paidFrom) => new()
    {
        Name = name,
        Kind = OutgoingKindDto.Bill,
        Priority = OutgoingPriorityDto.Essential,
        CategoryId = categoryId,
        Amount = 10m,
        Every = 1,
        Unit = PaymentUnitDto.Month,
        FirstPaymentDate = new DateOnly(2026, 1, 1),
        PaidFrom = paidFrom,
    };
}
