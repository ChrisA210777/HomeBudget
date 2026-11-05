using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Domain.Outgoings;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.Outgoings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.Outgoings;

[Collection(ApiCollection.Name)]
public class OutgoingCategoryApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task New_households_start_with_the_default_categories_in_order()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        var categories = await ListAsync(client, householdId);

        Assert.Equal(OutgoingCategory.DefaultNames, categories.Select(c => c.Name));
        Assert.All(categories, c =>
        {
            Assert.False(c.IsHidden);
            Assert.Equal(0, c.OutgoingCount);
        });
    }

    [Fact]
    public async Task Categories_can_be_added_renamed_hidden_and_shown()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var added = await client.PostAsJsonAsync(Url(OutgoingCategoryRoutes.Categories(householdId)), new AddCategoryRequest { Name = "  Hobbies " });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var hobbies = (await added.Content.ReadFromJsonAsync<OutgoingCategorySummary>())!;
        Assert.Equal("Hobbies", hobbies.Name);
        Assert.Equal("Hobbies", (await ListAsync(client, householdId))[^1].Name);

        using var updated = await client.PutAsJsonAsync(
            Url(OutgoingCategoryRoutes.Category(householdId, hobbies.Id)), new UpdateCategoryRequest { Name = "Hobbies & crafts", IsHidden = true });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var hidden = (await ListAsync(client, householdId)).Single(c => c.Id == hobbies.Id);
        Assert.Equal("Hobbies & crafts", hidden.Name);
        Assert.True(hidden.IsHidden);

        (await client.PutAsJsonAsync(Url(OutgoingCategoryRoutes.Category(householdId, hobbies.Id)), new UpdateCategoryRequest { Name = "Hobbies & crafts", IsHidden = false })).Dispose();
        Assert.False((await ListAsync(client, householdId)).Single(c => c.Id == hobbies.Id).IsHidden);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("housing")]
    public async Task Names_are_required_and_unique_ignoring_case(string name)
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.PostAsJsonAsync(Url(OutgoingCategoryRoutes.Categories(householdId)), new AddCategoryRequest { Name = name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Name", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Renaming_to_another_categorys_name_is_refused_but_keeping_your_own_is_fine()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        var pets = (await ListAsync(client, householdId)).Single(c => c.Name == "Pets");

        using var clash = await client.PutAsJsonAsync(Url(OutgoingCategoryRoutes.Category(householdId, pets.Id)), new UpdateCategoryRequest { Name = "Utilities" });
        using var same = await client.PutAsJsonAsync(Url(OutgoingCategoryRoutes.Category(householdId, pets.Id)), new UpdateCategoryRequest { Name = "PETS" });

        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
    }

    [Fact]
    public async Task Unused_categories_can_be_deleted_but_ones_in_use_cannot()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        var categories = await ListAsync(client, householdId);
        var children = categories.Single(c => c.Name == "Children");
        var insurance = categories.Single(c => c.Name == "Insurance");
        await AddOutgoingAsync(householdId, insurance.Id);

        using var deleted = await client.DeleteAsync(Url(OutgoingCategoryRoutes.Category(householdId, children.Id)));
        using var refused = await client.DeleteAsync(Url(OutgoingCategoryRoutes.Category(householdId, insurance.Id)));

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var after = await ListAsync(client, householdId);
        Assert.DoesNotContain(after, c => c.Id == children.Id);
        Assert.Equal(1, after.Single(c => c.Id == insurance.Id).OutgoingCount);
    }

    [Fact]
    public async Task Viewers_can_see_categories()
    {
        var (_, householdId) = await CreateHouseholdOwnerAsync();
        var viewerId = await AddMemberAsync(householdId, HouseholdRole.Viewer);
        using var client = CreateClient(signedInAs: viewerId);

        Assert.Equal(16, (await ListAsync(client, householdId)).Count);
    }

    [Fact]
    public Task Only_users_and_admins_can_add() =>
        AssertMinimumRoleAsync(HttpMethod.Post, OutgoingCategoryRoutes.Categories, HouseholdRole.User, () => new AddCategoryRequest { Name = $"New {Guid.NewGuid():N}"[..20] });

    [Fact]
    public async Task Other_households_cannot_see_or_change_categories()
    {
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, OutgoingCategoryRoutes.Categories);
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Post, OutgoingCategoryRoutes.Categories, new AddCategoryRequest { Name = "Sneaky" });
    }

    [Fact]
    public async Task Other_households_categories_are_not_found()
    {
        var (_, otherHousehold) = await CreateHouseholdOwnerAsync();
        Guid otherCategory = default;
        await ExecuteDbContextAsync(async db => otherCategory = await db.OutgoingCategories.IgnoreQueryFilters()
            .Where(c => c.HouseholdId == otherHousehold).Select(c => c.Id).FirstAsync());
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var update = await client.PutAsJsonAsync(Url(OutgoingCategoryRoutes.Category(householdId, otherCategory)), new UpdateCategoryRequest { Name = "Mine now" });
        using var delete = await client.DeleteAsync(Url(OutgoingCategoryRoutes.Category(householdId, otherCategory)));

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task An_outgoing_and_its_history_are_saved_and_loaded_intact()
    {
        var (_, householdId) = await CreateHouseholdOwnerAsync();
        Guid categoryId = default;
        await ExecuteDbContextAsync(async db => categoryId = await db.OutgoingCategories.IgnoreQueryFilters()
            .Where(c => c.HouseholdId == householdId && c.Name == "Housing").Select(c => c.Id).SingleAsync());

        var outgoing = Outgoing.Create(householdId, new OutgoingDetails
        {
            Name = "Mortgage",
            CategoryId = categoryId,
            FirstPaymentDate = new DateOnly(2026, 1, 1),
            RenewalDate = new DateOnly(2027, 3, 1),
            RenewalEveryMonths = 24,
            AutoRenews = false,
            AlertPeriod = AlertPeriod.ThreeMonths,
            Recipient = "Example Building Society",
            PaymentMethod = PaymentMethod.DirectDebit,
            PaidFrom = "Joint account",
            ReferenceLabel = "Mortgage account",
            ReferenceValue = "M-12345",
            Website = "example.com",
            ContactPhone = "0800 000 000",
            Notes = "Fixed rate until 2027",
        }, 900m, PaymentSchedule.Monthly, "Starting amount");
        outgoing.ChangeAmount(new DateOnly(2027, 3, 1), 1050m, PaymentSchedule.Monthly, "New fixed rate");
        outgoing.Pause(new DateOnly(2026, 12, 1), new DateOnly(2027, 2, 1), "Payment holiday");
        await ExecuteDbContextAsync(async db =>
        {
            db.Outgoings.Add(outgoing);
            await db.SaveChangesAsync();
        });

        await ExecuteDbContextAsync(async db =>
        {
            var loaded = await db.Outgoings.IgnoreQueryFilters()
                .Include(o => o.AmountChanges).Include(o => o.StatusPeriods).Include(o => o.Corrections)
                .SingleAsync(o => o.Id == outgoing.Id);

            Assert.Equal("Mortgage", loaded.Name);
            Assert.Equal(OutgoingKind.Bill, loaded.Kind);
            Assert.Equal(PaymentMethod.DirectDebit, loaded.PaymentMethod);
            Assert.Equal(AlertPeriod.ThreeMonths, loaded.AlertPeriod);
            Assert.Equal(24, loaded.RenewalEveryMonths);
            Assert.Equal("https://example.com/", loaded.Website);
            Assert.Equal(2, loaded.AmountChanges.Count);
            Assert.Equal(new DateOnly(2027, 2, 1), loaded.StatusPeriods.Single().ResumesOn);
            Assert.Equal(0m, loaded.MonthlyEquivalentForMonth(2027, 1));
            Assert.Equal(1050m, loaded.MonthlyEquivalentForMonth(2027, 3));
            Assert.Equal(PaymentUnit.Month, loaded.AmountChanges.First().Unit);
        });
    }

    private async Task AddOutgoingAsync(Guid householdId, Guid categoryId) =>
        await ExecuteDbContextAsync(async db =>
        {
            db.Outgoings.Add(Outgoing.Create(householdId, new OutgoingDetails
            {
                Name = "Home insurance",
                CategoryId = categoryId,
                FirstPaymentDate = new DateOnly(2026, 1, 1),
            }, 240m, PaymentSchedule.Yearly, "Starting amount"));
            await db.SaveChangesAsync();
        });

    private static async Task<List<OutgoingCategorySummary>> ListAsync(HttpClient client, Guid householdId) =>
        (await client.GetFromJsonAsync<List<OutgoingCategorySummary>>(Url(OutgoingCategoryRoutes.Categories(householdId))))!;

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
