using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Outgoings;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.Budgeting;
using Budget.Shared.Outgoings;
using Microsoft.AspNetCore.Mvc;

namespace Budget.Server.Tests.Outgoings;

/// <summary>Category icons (BUD-69).</summary>
[Collection(ApiCollection.Name)]
public class CategoryIconApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public void Every_default_category_icon_is_one_the_app_offers()
    {
        Assert.Equal(OutgoingCategory.DefaultNames.Order(), OutgoingCategory.DefaultIcons.Keys.Order());
        Assert.All(OutgoingCategory.DefaultIcons.Values, key => Assert.True(CategoryIcons.IsKnown(key), key));
        Assert.True(CategoryIcons.IsKnown(OutgoingCategory.DefaultIcon));
        Assert.Equal(CategoryIcons.Default, OutgoingCategory.DefaultIcon);
    }

    [Fact]
    public void Icon_keys_are_unique_and_fit_the_column()
    {
        Assert.Equal(CategoryIcons.All.Count, CategoryIcons.All.Select(i => i.Key).Distinct().Count());
        Assert.All(CategoryIcons.All, i => Assert.InRange(i.Key.Length, 1, OutgoingCategory.IconMaxLength));
    }

    [Fact]
    public async Task New_households_default_categories_have_their_icons()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        var categories = await ListAsync(client, householdId);

        Assert.All(categories, c => Assert.Equal(OutgoingCategory.DefaultIcons[c.Name], c.Icon));
        Assert.Equal("home", categories.Single(c => c.Name == "Housing").Icon);
    }

    [Fact]
    public async Task A_new_category_gets_the_icon_chosen_or_a_plain_label()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        var plain = await AddAsync(client, householdId, new AddCategoryRequest { Name = "Hobbies" });
        var chosen = await AddAsync(client, householdId, new AddCategoryRequest { Name = "Coffee", Icon = "coffee" });

        Assert.Equal(CategoryIcons.Default, plain.Icon);
        Assert.Equal("coffee", chosen.Icon);
        Assert.Equal("coffee", (await ListAsync(client, householdId)).Single(c => c.Id == chosen.Id).Icon);
    }

    [Fact]
    public async Task Changing_the_icon_is_saved_and_leaving_it_out_keeps_it()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        var housing = (await ListAsync(client, householdId)).Single(c => c.Name == "Housing");

        using var changed = await client.PutAsJsonAsync(Url(OutgoingCategoryRoutes.Category(householdId, housing.Id)),
            new UpdateCategoryRequest { Name = "Housing", Icon = "bank" });
        Assert.Equal("bank", (await changed.Content.ReadFromJsonAsync<OutgoingCategorySummary>())!.Icon);

        using var renamed = await client.PutAsJsonAsync(Url(OutgoingCategoryRoutes.Category(householdId, housing.Id)),
            new UpdateCategoryRequest { Name = "Home" });
        Assert.Equal("bank", (await renamed.Content.ReadFromJsonAsync<OutgoingCategorySummary>())!.Icon);
    }

    [Fact]
    public async Task Unknown_icons_are_refused()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        var housing = (await ListAsync(client, householdId)).Single(c => c.Name == "Housing");

        using var add = await client.PostAsJsonAsync(Url(OutgoingCategoryRoutes.Categories(householdId)), new AddCategoryRequest { Name = "Hobbies", Icon = "rocket" });
        using var update = await client.PutAsJsonAsync(Url(OutgoingCategoryRoutes.Category(householdId, housing.Id)), new UpdateCategoryRequest { Name = "Housing", Icon = "<svg/>" });

        foreach (var response in new[] { add, update })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
            Assert.Contains("Icon", problem!.Errors.Keys);
        }
    }

    [Fact]
    public async Task The_outgoings_list_and_budget_carry_each_categorys_icon()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        var housing = (await ListAsync(client, householdId)).Single(c => c.Name == "Housing");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await client.PostAsJsonAsync(Url(OutgoingRoutes.Outgoings(householdId)), new CreateOutgoingRequest
        {
            Kind = OutgoingKindDto.Bill,
            Name = "Rent",
            CategoryId = housing.Id,
            Priority = OutgoingPriorityDto.Essential,
            Amount = 900m,
            FirstPaymentDate = new DateOnly(today.Year, today.Month, 1),
        })).EnsureSuccessStatusCode();

        var month = await client.GetFromJsonAsync<OutgoingMonthResponse>(Url(OutgoingRoutes.ForMonth(householdId, today.Year, today.Month)));
        var budget = await client.GetFromJsonAsync<BudgetMonthResponse>(Url(BudgetRoutes.Month(householdId, today.Year, today.Month)));

        Assert.Equal("home", Assert.Single(month!.Items).CategoryIcon);
        Assert.Equal("home", Assert.Single(budget!.ByCategory).Icon);
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<List<OutgoingCategorySummary>> ListAsync(HttpClient client, Guid householdId) =>
        (await client.GetFromJsonAsync<List<OutgoingCategorySummary>>(Url(OutgoingCategoryRoutes.Categories(householdId))))!;

    private static async Task<OutgoingCategorySummary> AddAsync(HttpClient client, Guid householdId, AddCategoryRequest request)
    {
        using var response = await client.PostAsJsonAsync(Url(OutgoingCategoryRoutes.Categories(householdId)), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OutgoingCategorySummary>())!;
    }
}
