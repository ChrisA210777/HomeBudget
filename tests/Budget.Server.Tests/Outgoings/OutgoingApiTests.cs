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
public class OutgoingApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task A_bill_is_added_with_all_its_details()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var insurance = await CategoryIdAsync(householdId, "Insurance");
        var personId = await PersonIdAsync(householdId);
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.PostAsJsonAsync(Url(OutgoingRoutes.Outgoings(householdId)), Valid(insurance) with
        {
            Name = "  Car insurance ",
            PersonId = personId,
            Amount = 480m,
            Every = 1,
            Unit = PaymentUnitDto.Year,
            FirstPaymentDate = new DateOnly(2026, 3, 14),
            Recipient = "Example Insurance",
            PaymentMethod = PaymentMethodDto.DirectDebit,
            PaidFrom = "Joint account",
            ReferenceLabel = "Policy number",
            ReferenceValue = "POL-123",
            Website = "example.com",
            ContactPhone = "0800 000 000",
            Notes = "No claims since 2020",
            RenewalDate = new DateOnly(2027, 3, 14),
            RenewalEveryMonths = 12,
            AutoRenews = true,
            AlertPeriod = AlertPeriodDto.OneMonth,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<OutgoingCreatedResponse>())!;
        Assert.Equal("Car insurance", created.Name);
        Assert.Equal(40m, created.MonthlyAmount);

        await ExecuteDbContextAsync(async db =>
        {
            var saved = await db.Outgoings.IgnoreQueryFilters().Include(o => o.AmountChanges).SingleAsync(o => o.Id == created.Id);
            Assert.Equal(householdId, saved.HouseholdId);
            Assert.Equal(OutgoingKind.Bill, saved.Kind);
            Assert.Equal(OutgoingPriority.Essential, saved.Priority);
            Assert.Equal(insurance, saved.CategoryId);
            Assert.Equal(personId, saved.PersonId);
            Assert.Equal(PaymentMethod.DirectDebit, saved.PaymentMethod);
            Assert.Equal("Joint account", saved.PaidFrom);
            Assert.Equal("https://example.com/", saved.Website);
            Assert.Equal(new DateOnly(2027, 3, 14), saved.RenewalDate);
            Assert.True(saved.AutoRenews);
            Assert.Equal(AlertPeriod.OneMonth, saved.AlertPeriod);
            var first = Assert.Single(saved.AmountChanges);
            Assert.Equal(new DateOnly(2026, 3, 14), first.EffectiveFrom);
            Assert.Equal(480m, first.Amount);
            Assert.Equal(PaymentUnit.Year, first.Unit);
            Assert.Equal("Starting amount", first.Comment);
            Assert.Equal(ownerId, saved.CreatedBy);
        });
    }

    [Fact]
    public async Task An_allowance_for_the_household_needs_only_the_basics()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var food = await CategoryIdAsync(householdId, "Food & household");
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.PostAsJsonAsync(Url(OutgoingRoutes.Outgoings(householdId)), new CreateOutgoingRequest
        {
            Kind = OutgoingKindDto.Allowance,
            Name = "Food shopping",
            CategoryId = food,
            Priority = OutgoingPriorityDto.Essential,
            Amount = 120m,
            Every = 1,
            Unit = PaymentUnitDto.Week,
            FirstPaymentDate = new DateOnly(2026, 11, 2),
            Comment = "Rough average",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(520m, (await response.Content.ReadFromJsonAsync<OutgoingCreatedResponse>())!.MonthlyAmount);
    }

    [Fact]
    public async Task Missing_and_invalid_details_are_reported_by_field()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.PostAsJsonAsync(Url(OutgoingRoutes.Outgoings(householdId)), new CreateOutgoingRequest
        {
            Kind = OutgoingKindDto.Allowance,
            Every = 0,
            Website = "not a website",
            RenewalDate = new DateOnly(2027, 1, 1),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.Equivalent(
            new[] { "Name", "CategoryId", "Priority", "Amount", "Every", "FirstPaymentDate", "Website", "RenewalDate" },
            problem.Errors.Keys);
    }

    [Fact]
    public async Task Hidden_and_other_households_categories_and_people_are_refused()
    {
        var (_, otherHousehold) = await CreateHouseholdOwnerAsync();
        var otherCategory = await CategoryIdAsync(otherHousehold, "Pets");
        var otherPerson = await PersonIdAsync(otherHousehold);
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var pets = await CategoryIdAsync(householdId, "Pets");
        using var client = CreateClient(signedInAs: ownerId);
        (await client.PutAsJsonAsync(Url(OutgoingCategoryRoutes.Category(householdId, pets)), new UpdateCategoryRequest { Name = "Pets", IsHidden = true })).Dispose();

        foreach (var (category, person) in new[] { (otherCategory, (Guid?)null), (pets, null), (await CategoryIdAsync(householdId, "Housing"), otherPerson) })
        {
            using var response = await client.PostAsJsonAsync(Url(OutgoingRoutes.Outgoings(householdId)), Valid(category) with { PersonId = person });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
            Assert.Contains(person is null ? "CategoryId" : "PersonId", problem.Errors.Keys);
        }
    }

    [Fact]
    public async Task Paid_from_suggestions_are_the_households_account_names()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var housing = await CategoryIdAsync(householdId, "Housing");
        using var client = CreateClient(signedInAs: ownerId);
        foreach (var account in new[] { "Joint account", "Chris's current account", "Joint account", null })
        {
            (await client.PostAsJsonAsync(Url(OutgoingRoutes.Outgoings(householdId)), Valid(housing) with { PaidFrom = account })).Dispose();
        }

        var (otherOwner, otherHousehold) = await CreateHouseholdOwnerAsync();
        using var other = CreateClient(signedInAs: otherOwner);
        (await other.PostAsJsonAsync(Url(OutgoingRoutes.Outgoings(otherHousehold)), Valid(await CategoryIdAsync(otherHousehold, "Housing")) with { PaidFrom = "Secret account" })).Dispose();

        var names = await client.GetFromJsonAsync<List<string>>(Url(OutgoingRoutes.PaidFromNames(householdId)));

        Assert.Equal(new[] { "Chris's current account", "Joint account" }, names);
    }

    [Fact]
    public Task Only_users_and_admins_can_add() =>
        AssertMinimumRoleAsync(HttpMethod.Post, OutgoingRoutes.Outgoings, HouseholdRole.User, () => new CreateOutgoingRequest());

    [Fact]
    public Task Viewers_can_see_paid_from_suggestions() =>
        AssertMinimumRoleAsync(HttpMethod.Get, OutgoingRoutes.PaidFromNames, HouseholdRole.Viewer);

    [Fact]
    public async Task Other_households_cannot_add_or_see_suggestions()
    {
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Post, OutgoingRoutes.Outgoings, new CreateOutgoingRequest());
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, OutgoingRoutes.PaidFromNames);
    }

    [Theory]
    [InlineData(typeof(OutgoingKind), typeof(OutgoingKindDto))]
    [InlineData(typeof(OutgoingPriority), typeof(OutgoingPriorityDto))]
    [InlineData(typeof(PaymentUnit), typeof(PaymentUnitDto))]
    [InlineData(typeof(PaymentMethod), typeof(PaymentMethodDto))]
    [InlineData(typeof(AlertPeriod), typeof(AlertPeriodDto))]
    public void Shared_enums_match_the_domain(Type domain, Type shared)
    {
        Assert.Equal(Enum.GetNames(domain), Enum.GetNames(shared));
        Assert.Equal(Enum.GetValues(domain).Cast<int>(), Enum.GetValues(shared).Cast<int>());
    }

    private static CreateOutgoingRequest Valid(Guid categoryId) => new()
    {
        Kind = OutgoingKindDto.Bill,
        Name = "Something",
        CategoryId = categoryId,
        Priority = OutgoingPriorityDto.Essential,
        Amount = 10m,
        FirstPaymentDate = new DateOnly(2026, 11, 1),
    };

    private async Task<Guid> CategoryIdAsync(Guid householdId, string name)
    {
        Guid id = default;
        await ExecuteDbContextAsync(async db => id = await db.OutgoingCategories.IgnoreQueryFilters()
            .Where(c => c.HouseholdId == householdId && c.Name == name).Select(c => c.Id).SingleAsync());
        return id;
    }

    private async Task<Guid> PersonIdAsync(Guid householdId)
    {
        Guid id = default;
        await ExecuteDbContextAsync(async db => id = await db.People.IgnoreQueryFilters()
            .Where(p => p.HouseholdId == householdId).Select(p => p.Id).FirstAsync());
        return id;
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
