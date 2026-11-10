using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Account;
using Budget.Shared.Households;

namespace Budget.Server.Tests.Households;

/// <summary>PUT /api/households/{id}/settings: "Accounts and transfers" (BUD-91).</summary>
[Collection(ApiCollection.Name)]
public class HouseholdSettingsApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Accounts_and_transfers_is_off_until_an_admin_turns_it_on()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        Assert.False((await client.GetFromJsonAsync<HouseholdResponse>(Url(HouseholdRoutes.Household(householdId))))!.TransfersEnabled);

        using var response = await client.PutAsJsonAsync(Url(HouseholdRoutes.Settings(householdId)), new HouseholdSettingsRequest { TransfersEnabled = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<HouseholdResponse>())!.TransfersEnabled);
        var me = await client.GetFromJsonAsync<CurrentUserResponse>(Url(AccountRoutes.CurrentUser));
        Assert.True(me!.Households.Single(h => h.HouseholdId == householdId).TransfersEnabled);
    }

    [Fact]
    public async Task Only_admins_change_settings()
    {
        await AssertMinimumRoleAsync(HttpMethod.Put, HouseholdRoutes.Settings, HouseholdRole.Admin, () => new HouseholdSettingsRequest { TransfersEnabled = true });
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Put, HouseholdRoutes.Settings, new HouseholdSettingsRequest { TransfersEnabled = true });
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
