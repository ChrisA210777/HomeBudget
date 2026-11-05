using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.Households;
using Microsoft.AspNetCore.Mvc;

namespace Budget.Server.Tests.Households;

[Collection(ApiCollection.Name)]
public class RenameHouseholdTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task The_owner_can_rename_their_household_and_the_name_is_trimmed()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.PutAsJsonAsync(
            Url(householdId), new RenameHouseholdRequest { Name = "  Smith Family ", PreviousName = "My Household" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Smith Family", (await response.Content.ReadFromJsonAsync<HouseholdResponse>())!.Name);
        var reloaded = await client.GetFromJsonAsync<HouseholdResponse>(Url(householdId));
        Assert.Equal("Smith Family", reloaded!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_name_is_required(string name)
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.PutAsJsonAsync(Url(householdId), new RenameHouseholdRequest { Name = name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(nameof(RenameHouseholdRequest.Name), problem!.Errors.Keys);
    }

    [Fact]
    public async Task A_name_can_be_at_most_100_characters()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.PutAsJsonAsync(Url(householdId), new RenameHouseholdRequest { Name = new string('a', 101) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Someone_elses_rename_is_not_overwritten()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        (await client.PutAsJsonAsync(Url(householdId), new RenameHouseholdRequest { Name = "Smiths", PreviousName = "My Household" })).Dispose();

        using var response = await client.PutAsJsonAsync(
            Url(householdId), new RenameHouseholdRequest { Name = "The Smiths", PreviousName = "My Household" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var reloaded = await client.GetFromJsonAsync<HouseholdResponse>(Url(householdId));
        Assert.Equal("Smiths", reloaded!.Name);
    }

    [Fact]
    public Task Other_households_cannot_rename_it() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Put, HouseholdRoutes.Household, new RenameHouseholdRequest { Name = "Hijacked" });

    [Fact]
    public Task Only_admins_can_rename_it() =>
        AssertMinimumRoleAsync(HttpMethod.Put, HouseholdRoutes.Household, HouseholdRole.Admin, () => new RenameHouseholdRequest { Name = "Renamed" });

    private static Uri Url(Guid householdId) => new(HouseholdRoutes.Household(householdId), UriKind.Relative);
}
