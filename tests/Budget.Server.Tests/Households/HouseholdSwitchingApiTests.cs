using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Account;
using Budget.Shared.Households;
using Budget.Shared.Outgoings;
using Budget.Shared.People;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.Households;

/// <summary>Switching household (BUD-20) and creating another one (BUD-21).</summary>
[Collection(ApiCollection.Name)]
public class HouseholdSwitchingApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task A_new_household_is_set_up_like_the_first_and_becomes_active()
    {
        var (userId, firstHouseholdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        using var client = CreateClient(signedInAs: userId);

        using var response = await client.PostAsJsonAsync(Url(HouseholdRoutes.Households), new CreateHouseholdRequest { Name = "  Mum's budget " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<HouseholdResponse>();
        Assert.NotNull(created);
        Assert.Equal(("Mum's budget", "GBP", "Admin", true, 1), (created.Name, created.CurrencyCode, created.Role, created.IsOwner, created.MemberCount));
        Assert.Equal(HouseholdRoutes.Household(created.Id), response.Headers.Location?.OriginalString);

        var me = await client.GetFromJsonAsync<CurrentUserResponse>(Url(AccountRoutes.CurrentUser));
        Assert.Equal(created.Id, me!.LastActiveHouseholdId);
        Assert.Equal(new[] { firstHouseholdId, created.Id }.Order(), me.Households.Select(h => h.HouseholdId).Order());

        var people = await client.GetFromJsonAsync<List<PersonSummary>>(Url(PeopleRoutes.People(created.Id)));
        var person = Assert.Single(people!);
        Assert.Equal(("Chris Austin", userId), (person.DisplayName, person.LinkedUserId));

        var categories = await client.GetFromJsonAsync<List<OutgoingCategorySummary>>(Url(OutgoingCategoryRoutes.Categories(created.Id)));
        Assert.NotEmpty(categories!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_name_is_required(string name)
    {
        var (userId, _) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: userId);

        using var response = await client.PostAsJsonAsync(Url(HouseholdRoutes.Households), new CreateHouseholdRequest { Name = name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Name", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Signed_out_people_cant_create_households()
    {
        using var client = CreateClient(followRedirects: false);

        using var response = await client.PostAsJsonAsync(Url(HouseholdRoutes.Households), new CreateHouseholdRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Switching_household_is_remembered()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Owner");
        var (memberId, ownHouseholdId) = await CreateHouseholdOwnerAsync("Member");
        await JoinAsync(householdId, memberId);
        using var member = CreateClient(signedInAs: memberId);

        using var response = await member.PutAsJsonAsync(Url(AccountRoutes.ActiveHousehold), new SetActiveHouseholdRequest(householdId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var me = await member.GetFromJsonAsync<CurrentUserResponse>(Url(AccountRoutes.CurrentUser));
        Assert.Equal(householdId, me!.LastActiveHouseholdId);
        Assert.Equal(2, me.Households.Count);
        Assert.Contains(me.Households, h => h.HouseholdId == ownHouseholdId);
        Assert.NotEqual(Guid.Empty, ownerId);
    }

    [Fact]
    public async Task You_cant_switch_to_a_household_you_dont_belong_to()
    {
        var (_, otherHouseholdId) = await CreateHouseholdOwnerAsync("Someone else");
        var (userId, ownHouseholdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: userId);

        foreach (var householdId in new[] { otherHouseholdId, Guid.NewGuid() })
        {
            using var response = await client.PutAsJsonAsync(Url(AccountRoutes.ActiveHousehold), new SetActiveHouseholdRequest(householdId));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var me = await client.GetFromJsonAsync<CurrentUserResponse>(Url(AccountRoutes.CurrentUser));
        Assert.Equal(ownHouseholdId, me!.LastActiveHouseholdId);
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private Task JoinAsync(Guid householdId, Guid userId) =>
        ExecuteDbContextAsync(async db =>
        {
            var household = await db.Households.Include(h => h.Members).SingleAsync(h => h.Id == householdId);
            household.AddMember(userId, HouseholdRole.Viewer, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        });
}
