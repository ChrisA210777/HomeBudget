using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Account;
using Budget.Shared.Households;
using Budget.Shared.Income;
using Budget.Shared.Outgoings;
using Budget.Shared.People;

namespace Budget.Server.Tests.Households;

/// <summary>GET /api/households/{id}/setup, for the home page's getting-started prompts (BUD-68).</summary>
[Collection(ApiCollection.Name)]
public class HouseholdSetupApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task A_new_household_has_only_its_owner()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        var setup = await client.GetFromJsonAsync<HouseholdSetupResponse>(Url(HouseholdRoutes.Setup(householdId)));

        Assert.Equal(new HouseholdSetupResponse(0, 0, 1), setup);
    }

    [Fact]
    public async Task Counts_everything_including_items_that_havent_started()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        var me = await client.GetFromJsonAsync<CurrentUserResponse>(Url(AccountRoutes.CurrentUser));
        var personId = me!.Households.Single().PersonId!.Value;

        (await client.PostAsJsonAsync(Url(PeopleRoutes.People(householdId)), new SavePersonRequest { DisplayName = "Jane" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(Url(IncomeRoutes.IncomeStreams(householdId)), new CreateIncomeStreamRequest
        {
            PersonId = personId,
            Name = "New job",
            Amount = 2500m,
            StartsOn = new DateOnly(2030, 1, 1),
        })).EnsureSuccessStatusCode();
        var categories = await client.GetFromJsonAsync<List<OutgoingCategorySummary>>(Url(OutgoingCategoryRoutes.Categories(householdId)));
        (await client.PostAsJsonAsync(Url(OutgoingRoutes.Outgoings(householdId)), new CreateOutgoingRequest
        {
            Kind = OutgoingKindDto.Bill,
            Name = "Council tax",
            CategoryId = categories![0].Id,
            Priority = OutgoingPriorityDto.Essential,
            Amount = 150m,
            FirstPaymentDate = new DateOnly(2030, 1, 1),
        })).EnsureSuccessStatusCode();

        var setup = await client.GetFromJsonAsync<HouseholdSetupResponse>(Url(HouseholdRoutes.Setup(householdId)));

        Assert.Equal(new HouseholdSetupResponse(1, 1, 2), setup);
    }

    [Fact]
    public Task Other_households_cant_read_it() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, HouseholdRoutes.Setup);

    [Fact]
    public Task Every_role_can_read_it() =>
        AssertMinimumRoleAsync(HttpMethod.Get, HouseholdRoutes.Setup, HouseholdRole.Viewer);

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
