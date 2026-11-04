using System.Net;
using System.Net.Http.Json;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Account;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.Account;

[Collection(ApiCollection.Name)]
public class CurrentUserTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly Uri CurrentUser = new(AccountRoutes.CurrentUser, UriKind.Relative);

    [Fact]
    public async Task Returns_401_when_not_signed_in()
    {
        using var client = CreateClient(followRedirects: false);

        using var response = await client.GetAsync(CurrentUser);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_the_signed_in_user()
    {
        var user = await CreateUserAsync("Chris Austin", "chris@example.com");
        using var client = CreateClient(signedInAs: user.Id);

        var response = await client.GetFromJsonAsync<CurrentUserResponse>(CurrentUser);

        Assert.NotNull(response);
        Assert.Equal(user.Id, response.Id);
        Assert.Equal("Chris Austin", response.DisplayName);
        Assert.Equal("chris@example.com", response.Email);
        Assert.Empty(response.Households);
    }

    [Fact]
    public async Task New_user_sees_My_Household_as_their_active_household()
    {
        var (userId, _) = await CreateHouseholdOwnerAsync("Pat Lee");
        using var client = CreateClient(signedInAs: userId);

        var response = await client.GetFromJsonAsync<CurrentUserResponse>(CurrentUser);

        Assert.NotNull(response);
        var household = Assert.Single(response.Households);
        Assert.Equal("My Household", household.Name);
        Assert.Equal("Admin", household.Role);
        Assert.True(household.IsOwner);
        Assert.Equal(household.HouseholdId, response.LastActiveHouseholdId);
    }

    [Fact]
    public async Task Active_household_falls_back_to_one_they_belong_to()
    {
        var (userId, _) = await CreateHouseholdOwnerAsync("Sam Ray");
        await ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.LastActiveHouseholdId = null;
            await db.SaveChangesAsync();
        });
        using var client = CreateClient(signedInAs: userId);

        var response = await client.GetFromJsonAsync<CurrentUserResponse>(CurrentUser);

        Assert.NotNull(response);
        Assert.Equal(Assert.Single(response.Households).HouseholdId, response.LastActiveHouseholdId);
    }

    [Fact]
    public async Task Returns_401_when_the_signed_in_user_no_longer_exists()
    {
        using var client = CreateClient(signedInAs: Guid.NewGuid(), followRedirects: false);

        using var response = await client.GetAsync(CurrentUser);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
