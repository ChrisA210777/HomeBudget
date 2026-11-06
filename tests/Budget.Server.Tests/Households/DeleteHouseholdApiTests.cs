using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Account;
using Budget.Shared.Households;
using Budget.Shared.Income;
using Budget.Shared.Outgoings;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.Households;

/// <summary>DELETE /api/households/{id} (BUD-22).</summary>
[Collection(ApiCollection.Name)]
public class DeleteHouseholdApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task The_owner_can_delete_it_with_everything_in_it()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var memberId = await AddMemberAsync(householdId, HouseholdRole.User);
        await SetActiveHouseholdAsync(memberId, householdId);
        using var owner = CreateClient(signedInAs: ownerId);
        using var member = CreateClient(signedInAs: memberId);
        await AddIncomeAndOutgoingAsync(owner, householdId);

        using var response = await owner.DeleteAsync(Url(HouseholdRoutes.Household(householdId)));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var afterwards = await member.GetAsync(Url(HouseholdRoutes.Household(householdId)));
        Assert.Equal(HttpStatusCode.NotFound, afterwards.StatusCode);

        await ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.Households.AnyAsync(h => h.Id == householdId));
            Assert.False(await db.HouseholdMembers.AnyAsync(m => m.HouseholdId == householdId));
            Assert.False(await db.People.IgnoreQueryFilters().AnyAsync(p => p.HouseholdId == householdId));
            Assert.False(await db.IncomeStreams.IgnoreQueryFilters().AnyAsync(i => i.HouseholdId == householdId));
            Assert.False(await db.Outgoings.IgnoreQueryFilters().AnyAsync(o => o.HouseholdId == householdId));
            Assert.False(await db.OutgoingCategories.IgnoreQueryFilters().AnyAsync(c => c.HouseholdId == householdId));
            Assert.False(await db.Users.AnyAsync(u => u.LastActiveHouseholdId == householdId));
        });

        // The owner had no other household: they're offered to create one.
        var me = await owner.GetFromJsonAsync<CurrentUserResponse>(Url(AccountRoutes.CurrentUser));
        Assert.Empty(me!.Households);
        Assert.Null(me.LastActiveHouseholdId);
    }

    [Fact]
    public async Task Other_households_are_untouched()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var (otherOwnerId, otherHouseholdId) = await CreateHouseholdOwnerAsync("Someone else");
        using var owner = CreateClient(signedInAs: ownerId);
        using var other = CreateClient(signedInAs: otherOwnerId);
        await AddIncomeAndOutgoingAsync(other, otherHouseholdId);

        using var response = await owner.DeleteAsync(Url(HouseholdRoutes.Household(householdId)));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var stillThere = await other.GetFromJsonAsync<HouseholdResponse>(Url(HouseholdRoutes.Household(otherHouseholdId)));
        Assert.Equal(otherHouseholdId, stillThere!.Id);
        await ExecuteDbContextAsync(async db =>
        {
            Assert.True(await db.Outgoings.IgnoreQueryFilters().AnyAsync(o => o.HouseholdId == otherHouseholdId));
            Assert.True(await db.IncomeStreams.IgnoreQueryFilters().AnyAsync(i => i.HouseholdId == otherHouseholdId));
        });
    }

    [Fact]
    public async Task Only_the_owner_can_delete_it()
    {
        var (_, householdId) = await CreateHouseholdOwnerAsync();

        foreach (var role in Enum.GetValues<HouseholdRole>())
        {
            var memberId = await AddMemberAsync(householdId, role, $"{role} member");
            using var client = CreateClient(signedInAs: memberId);

            using var response = await client.DeleteAsync(Url(HouseholdRoutes.Household(householdId)));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public Task Other_households_cant_delete_it() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Delete, HouseholdRoutes.Household);

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task AddIncomeAndOutgoingAsync(HttpClient client, Guid householdId)
    {
        var me = await client.GetFromJsonAsync<CurrentUserResponse>(Url(AccountRoutes.CurrentUser));
        var personId = me!.Households.Single(h => h.HouseholdId == householdId).PersonId!.Value;

        using var income = await client.PostAsJsonAsync(Url(IncomeRoutes.IncomeStreams(householdId)), new CreateIncomeStreamRequest
        {
            PersonId = personId,
            Name = "Salary",
            Amount = 2500m,
            StartsOn = new DateOnly(2026, 1, 1),
        });
        income.EnsureSuccessStatusCode();

        var categories = await client.GetFromJsonAsync<List<OutgoingCategorySummary>>(Url(OutgoingCategoryRoutes.Categories(householdId)));
        using var outgoing = await client.PostAsJsonAsync(Url(OutgoingRoutes.Outgoings(householdId)), new CreateOutgoingRequest
        {
            Kind = OutgoingKindDto.Bill,
            Name = "Council tax",
            CategoryId = categories![0].Id,
            Priority = OutgoingPriorityDto.Essential,
            Amount = 150m,
            FirstPaymentDate = new DateOnly(2026, 1, 1),
        });
        outgoing.EnsureSuccessStatusCode();
    }

    private Task SetActiveHouseholdAsync(Guid userId, Guid householdId) =>
        ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.LastActiveHouseholdId = householdId;
            await db.SaveChangesAsync();
        });
}
