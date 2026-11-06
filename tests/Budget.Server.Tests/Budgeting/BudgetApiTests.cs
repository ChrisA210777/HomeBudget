using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.Budgeting;
using Budget.Shared.Income;
using Budget.Shared.Outgoings;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.Budgeting;

[Collection(ApiCollection.Name)]
public class BudgetApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task The_budget_shows_income_less_outgoings_by_priority_kind_and_category()
    {
        var (client, householdId) = await HouseholdAsync();
        await AddIncomeAsync(client, householdId, 3000m);
        await AddOutgoingAsync(client, householdId, "Mortgage", "Housing", OutgoingPriorityDto.Essential, 900m, PaymentUnitDto.Month);
        await AddOutgoingAsync(client, householdId, "Council tax", "Housing", OutgoingPriorityDto.Essential, 150m, PaymentUnitDto.Month);
        await AddOutgoingAsync(client, householdId, "Car insurance", "Insurance", OutgoingPriorityDto.Essential, 480m, PaymentUnitDto.Year);
        await AddOutgoingAsync(client, householdId, "Food shopping", "Food & household", OutgoingPriorityDto.Preferred, 120m, PaymentUnitDto.Week, OutgoingKindDto.Allowance);
        await AddOutgoingAsync(client, householdId, "Holiday fund", "Savings & set-asides", OutgoingPriorityDto.Optional, 100m, PaymentUnitDto.Month, OutgoingKindDto.Savings);

        var budget = (await client.GetFromJsonAsync<BudgetMonthResponse>(Url(BudgetRoutes.Month(householdId, 2026, 11))))!;

        Assert.Equal((2026, 11, "GBP"), (budget.Year, budget.Month, budget.CurrencyCode));
        Assert.Equal(3000m, budget.Income);
        Assert.Equal((1090m, 520m, 100m), (budget.Essential, budget.Preferred, budget.Optional));
        Assert.Equal((1910m, 1390m, 1290m), (budget.LeftAfterEssential, budget.LeftAfterPreferred, budget.LeftAfterEverything));
        Assert.Equal(1710m, budget.Outgoings);
        Assert.Equal(
            new[] { (OutgoingKindDto.Bill, 1090m), (OutgoingKindDto.Savings, 100m), (OutgoingKindDto.Allowance, 520m) },
            budget.ByKind.Select(k => (k.Kind, k.Amount)));
        Assert.Equal(
            new[] { ("Housing", 1050m), ("Insurance", 40m), ("Food & household", 520m), ("Savings & set-asides", 100m) },
            budget.ByCategory.Select(c => (c.Name, c.Amount)));
    }

    [Fact]
    public async Task Spending_more_than_comes_in_is_a_negative_amount_left()
    {
        var (client, householdId) = await HouseholdAsync();
        await AddIncomeAsync(client, householdId, 1000m);
        await AddOutgoingAsync(client, householdId, "Rent", "Housing", OutgoingPriorityDto.Essential, 1200m, PaymentUnitDto.Month);

        var budget = (await client.GetFromJsonAsync<BudgetMonthResponse>(Url(BudgetRoutes.Month(householdId, 2026, 11))))!;

        Assert.Equal(-200m, budget.LeftAfterEssential);
        Assert.Equal(-200m, budget.LeftAfterEverything);
    }

    [Fact]
    public async Task Paused_and_not_yet_started_outgoings_count_nothing_and_categories_without_any_are_left_out()
    {
        var (client, householdId) = await HouseholdAsync();
        var gym = await AddOutgoingAsync(client, householdId, "Gym", "Leisure & memberships", OutgoingPriorityDto.Optional, 30m, PaymentUnitDto.Month);
        await AddOutgoingAsync(client, householdId, "New car", "Car & travel", OutgoingPriorityDto.Essential, 250m, PaymentUnitDto.Month, firstPayment: new DateOnly(2027, 1, 1));
        await ExecuteDbContextAsync(async db =>
        {
            var outgoing = await db.Outgoings.IgnoreQueryFilters().Include(o => o.AmountChanges).Include(o => o.StatusPeriods).SingleAsync(o => o.Id == gym);
            outgoing.PauseFor(new DateOnly(2026, 11, 5), payments: 1, "Injured");
            await db.SaveChangesAsync();
        });

        var november = (await client.GetFromJsonAsync<BudgetMonthResponse>(Url(BudgetRoutes.Month(householdId, 2026, 11))))!;
        var january = (await client.GetFromJsonAsync<BudgetMonthResponse>(Url(BudgetRoutes.Month(householdId, 2027, 1))))!;

        Assert.Equal(0m, november.Outgoings);
        Assert.Empty(november.ByCategory);
        Assert.Equal((250m, 30m), (january.Essential, january.Optional));
        Assert.Equal(new[] { "Car & travel", "Leisure & memberships" }, january.ByCategory.Select(c => c.Name));
    }

    [Fact]
    public async Task The_figures_match_the_income_and_outgoings_pages()
    {
        var (client, householdId) = await HouseholdAsync();
        await AddIncomeAsync(client, householdId, 2345.67m);
        await AddOutgoingAsync(client, householdId, "Water", "Utilities", OutgoingPriorityDto.Essential, 33.33m, PaymentUnitDto.Week);
        await AddOutgoingAsync(client, householdId, "Boiler service", "Home maintenance", OutgoingPriorityDto.Preferred, 89.99m, PaymentUnitDto.Year);

        var budget = (await client.GetFromJsonAsync<BudgetMonthResponse>(Url(BudgetRoutes.Month(householdId, 2026, 11))))!;
        var income = (await client.GetFromJsonAsync<IncomeMonthResponse>(Url(IncomeRoutes.ForMonth(householdId, 2026, 11))))!;
        var outgoings = (await client.GetFromJsonAsync<OutgoingMonthResponse>(Url(OutgoingRoutes.ForMonth(householdId, 2026, 11))))!;
        var totals = OutgoingTotals.For(outgoings.Items);

        Assert.Equal(income.Total, budget.Income);
        Assert.Equal((totals.Essential, totals.Preferred, totals.Optional), (budget.Essential, budget.Preferred, budget.Optional));
    }

    [Fact]
    public async Task This_month_is_the_default_and_a_bad_month_is_rejected()
    {
        var (client, householdId) = await HouseholdAsync();
        var today = DateTime.UtcNow;

        var budget = (await client.GetFromJsonAsync<BudgetMonthResponse>(Url(BudgetRoutes.Month(householdId))))!;
        using var bad = await client.GetAsync(Url(BudgetRoutes.Month(householdId) + "?month=nope"));

        Assert.Equal((today.Year, today.Month), (budget.Year, budget.Month));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public Task Viewers_can_see_the_budget() =>
        AssertMinimumRoleAsync(HttpMethod.Get, hh => BudgetRoutes.Month(hh), HouseholdRole.Viewer);

    [Fact]
    public Task Other_households_cannot_see_the_budget() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, hh => BudgetRoutes.Month(hh));

    private async Task<(HttpClient Client, Guid HouseholdId)> HouseholdAsync()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        return (CreateClient(signedInAs: ownerId), householdId);
    }

    private async Task AddIncomeAsync(HttpClient client, Guid householdId, decimal monthly)
    {
        Guid personId = default;
        await ExecuteDbContextAsync(async db => personId = await db.People.IgnoreQueryFilters().Where(p => p.HouseholdId == householdId).Select(p => p.Id).FirstAsync());
        using var response = await client.PostAsJsonAsync(Url(IncomeRoutes.IncomeStreams(householdId)), new CreateIncomeStreamRequest
        {
            Name = "Salary",
            PersonId = personId,
            Amount = monthly,
            StartsOn = new DateOnly(2026, 1, 1),
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> AddOutgoingAsync(
        HttpClient client, Guid householdId, string name, string category, OutgoingPriorityDto priority, decimal amount, PaymentUnitDto unit,
        OutgoingKindDto kind = OutgoingKindDto.Bill, DateOnly? firstPayment = null)
    {
        var categories = (await client.GetFromJsonAsync<List<OutgoingCategorySummary>>(Url(OutgoingCategoryRoutes.Categories(householdId))))!;
        using var response = await client.PostAsJsonAsync(Url(OutgoingRoutes.Outgoings(householdId)), new CreateOutgoingRequest
        {
            Kind = kind,
            Name = name,
            CategoryId = categories.Single(c => c.Name == category).Id,
            Priority = priority,
            Amount = amount,
            Unit = unit,
            FirstPaymentDate = firstPayment ?? new DateOnly(2026, 1, 5),
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OutgoingCreatedResponse>())!.Id;
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
