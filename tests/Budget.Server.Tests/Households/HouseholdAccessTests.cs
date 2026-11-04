using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.Households;

namespace Budget.Server.Tests.Households;

/// <summary>Household isolation and roles, checked through GET /api/households/{id}.</summary>
[Collection(ApiCollection.Name)]
public class HouseholdAccessTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Owner_can_read_their_household()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        var household = await client.GetFromJsonAsync<HouseholdResponse>(new Uri(HouseholdRoutes.Household(householdId), UriKind.Relative));

        Assert.NotNull(household);
        Assert.Equal(householdId, household.Id);
        Assert.Equal("My Household", household.Name);
        Assert.Equal("GBP", household.CurrencyCode);
        Assert.Equal("Admin", household.Role);
        Assert.True(household.IsOwner);
        Assert.Equal(1, household.MemberCount);
    }

    [Fact]
    public async Task Members_see_their_own_role()
    {
        var (_, householdId) = await CreateHouseholdOwnerAsync();
        var viewerId = await AddMemberAsync(householdId, HouseholdRole.Viewer);
        using var client = CreateClient(signedInAs: viewerId);

        var household = await client.GetFromJsonAsync<HouseholdResponse>(new Uri(HouseholdRoutes.Household(householdId), UriKind.Relative));

        Assert.NotNull(household);
        Assert.Equal("Viewer", household.Role);
        Assert.False(household.IsOwner);
        Assert.Equal(2, household.MemberCount);
    }

    [Fact]
    public Task Other_households_cannot_read_it() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, HouseholdRoutes.Household);

    [Fact]
    public Task Every_role_can_read_it() =>
        AssertMinimumRoleAsync(HttpMethod.Get, HouseholdRoutes.Household, HouseholdRole.Viewer);

    [Fact]
    public async Task Signed_out_requests_get_401()
    {
        var (_, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(followRedirects: false);

        using var response = await client.GetAsync(new Uri(HouseholdRoutes.Household(householdId), UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
