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
    public async Task Renewals_coming_up_are_listed_soonest_first()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var insurance = await CategoryIdAsync(householdId, "Insurance");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = today.AddYears(-1);

        Outgoing Renewing(string name, int inDays, AlertPeriod alert, bool autoRenews) =>
            Outgoing.Create(householdId, Details(name, insurance, start) with
            {
                RenewalDate = today.AddDays(inDays),
                AlertPeriod = alert,
                AutoRenews = autoRenews,
            }, 120m, PaymentSchedule.Yearly, "Start");

        var car = Renewing("Car insurance", 20, AlertPeriod.OneMonth, autoRenews: true);
        var tv = Renewing("TV Licence", 3, AlertPeriod.OneWeek, autoRenews: false);
        var later = Renewing("Home insurance", 60, AlertPeriod.OneMonth, autoRenews: true);
        var noReminder = Renewing("Breakdown cover", 2, AlertPeriod.None, autoRenews: true);
        var paused = Renewing("Gym", 5, AlertPeriod.OneMonth, autoRenews: true);
        paused.Pause(start, null, "Not going");
        await ExecuteDbContextAsync(async db =>
        {
            db.Outgoings.AddRange(car, tv, later, noReminder, paused);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient(signedInAs: ownerId);

        var comingUp = (await client.GetFromJsonAsync<List<RenewalComingUp>>(Url(OutgoingRoutes.ComingUp(householdId))))!;

        Assert.Equal(new[] { "TV Licence", "Car insurance" }, comingUp.Select(r => r.Name));
        Assert.Equal((today.AddDays(3), 3, false), (comingUp[0].RenewalDate, comingUp[0].DaysUntil, comingUp[0].AutoRenews));
        Assert.Equal((20, true, 120m, PaymentUnitDto.Year), (comingUp[1].DaysUntil, comingUp[1].AutoRenews, comingUp[1].Amount, comingUp[1].Unit));
    }

    [Fact]
    public async Task A_past_renewal_date_rolls_forward_by_the_cycle()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var insurance = await CategoryIdAsync(householdId, "Insurance");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var outgoing = Outgoing.Create(householdId, Details("Car insurance", insurance, today.AddYears(-2)) with
        {
            RenewalDate = today.AddDays(10).AddYears(-1),
            AlertPeriod = AlertPeriod.OneMonth,
        }, 480m, PaymentSchedule.Yearly, "Start");
        await ExecuteDbContextAsync(async db =>
        {
            db.Outgoings.Add(outgoing);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient(signedInAs: ownerId);

        var comingUp = (await client.GetFromJsonAsync<List<RenewalComingUp>>(Url(OutgoingRoutes.ComingUp(householdId))))!;

        Assert.Equal(today.AddDays(10), Assert.Single(comingUp).RenewalDate);
    }

    [Fact]
    public async Task Anyone_in_the_household_can_see_renewals_but_not_other_households()
    {
        await AssertMinimumRoleAsync(HttpMethod.Get, OutgoingRoutes.ComingUp, HouseholdRole.Viewer);
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, OutgoingRoutes.ComingUp);
    }

    [Fact]
    public async Task Reference_names_are_suggested_most_used_first()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var housing = await CategoryIdAsync(householdId, "Housing");
        using var client = CreateClient(signedInAs: ownerId);
        foreach (var label in new[] { "Account number", "Policy number", "policy number", null, "Policy number" })
        {
            (await client.PostAsJsonAsync(Url(OutgoingRoutes.Outgoings(householdId)), Valid(housing) with { ReferenceLabel = label })).Dispose();
        }

        var labels = await client.GetFromJsonAsync<List<string>>(Url(OutgoingRoutes.ReferenceLabels(householdId)));

        Assert.Equal(2, labels!.Count);
        Assert.Equal("policy number", labels[0], ignoreCase: true);
        Assert.Equal("Account number", labels[1]);
    }

    [Fact]
    public async Task Reference_names_are_only_the_households_own()
    {
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, OutgoingRoutes.ReferenceLabels);
        await AssertMinimumRoleAsync(HttpMethod.Get, OutgoingRoutes.ReferenceLabels, HouseholdRole.Viewer);
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

    [Fact]
    public async Task A_month_lists_outgoings_by_category_with_monthly_amounts_and_notes()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var housing = await CategoryIdAsync(householdId, "Housing");
        var insurance = await CategoryIdAsync(householdId, "Insurance");
        var food = await CategoryIdAsync(householdId, "Food & household");
        var personId = await PersonIdAsync(householdId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var mortgage = Outgoing.Create(householdId, Details("Mortgage", housing, new DateOnly(2026, 1, 1)), 900m, PaymentSchedule.Monthly, "Start");
        mortgage.PauseFor(new DateOnly(2026, 11, 1), payments: 1, "Payment holiday");
        var carInsurance = Outgoing.Create(householdId, Details("Car insurance", insurance, new DateOnly(2026, 3, 14)) with
        {
            PersonId = personId,
            Priority = OutgoingPriority.Essential,
            RenewalDate = today.AddDays(10),
            AlertPeriod = AlertPeriod.OneMonth,
        }, 480m, PaymentSchedule.Yearly, "Start");
        var councilTax = Outgoing.Create(householdId, Details("Council tax", housing, new DateOnly(2026, 4, 1)), 150m, PaymentSchedule.Monthly, "Start");
        councilTax.ChangeAmount(new DateOnly(2026, 11, 16), 180m, PaymentSchedule.Monthly, "New band");
        var food1 = Outgoing.Create(householdId, Details("Food shopping", food, new DateOnly(2026, 1, 5)) with
        {
            Kind = OutgoingKind.Allowance,
            Priority = OutgoingPriority.Preferred,
        }, 120m, PaymentSchedule.Weekly, "Start");
        var future = Outgoing.Create(householdId, Details("Gym", food, new DateOnly(2027, 1, 1)), 30m, PaymentSchedule.Monthly, "Start");
        var ended = Outgoing.Create(householdId, Details("Old phone", food, new DateOnly(2025, 1, 1)), 20m, PaymentSchedule.Monthly, "Start");
        ended.End(new DateOnly(2026, 6, 1), "Cancelled");
        await ExecuteDbContextAsync(async db =>
        {
            db.Outgoings.AddRange(mortgage, carInsurance, councilTax, food1, future, ended);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient(signedInAs: ownerId);

        var month = (await client.GetFromJsonAsync<OutgoingMonthResponse>(Url(OutgoingRoutes.ForMonth(householdId, 2026, 11))))!;

        Assert.Equal((2026, 11, "GBP"), (month.Year, month.Month, month.CurrencyCode));
        Assert.Equal(new[] { "Council tax", "Mortgage", "Car insurance", "Food shopping" }, month.Items.Select(i => i.Name));

        var councilTaxItem = month.Items[0];
        Assert.Equal(("Housing", 150m, 180m, PaymentUnitDto.Month), (councilTaxItem.CategoryName, councilTaxItem.MonthlyAmount, councilTaxItem.Amount, councilTaxItem.Unit));
        Assert.Equal(new[] { "Rises to £180.00 a month from 16 Nov 2026" }, councilTaxItem.Notes);

        var mortgageItem = month.Items[1];
        Assert.Equal(0m, mortgageItem.MonthlyAmount);
        Assert.Equal(new[] { "Paused from 1 Nov 2026 until 1 Dec 2026" }, mortgageItem.Notes);

        var insuranceItem = month.Items[2];
        Assert.Equal((40m, 480m, PaymentUnitDto.Year), (insuranceItem.MonthlyAmount, insuranceItem.Amount, insuranceItem.Unit));
        Assert.Equal(personId, insuranceItem.PersonId);
        Assert.False(string.IsNullOrEmpty(insuranceItem.PersonName));
        Assert.True(insuranceItem.IsComingUp);
        Assert.Equal(today.AddDays(10), insuranceItem.NextRenewal);
        Assert.Empty(insuranceItem.Notes);

        var foodItem = month.Items[3];
        Assert.Equal((OutgoingKindDto.Allowance, OutgoingPriorityDto.Preferred, 520m), (foodItem.Kind, foodItem.Priority, foodItem.MonthlyAmount));
        Assert.False(foodItem.IsComingUp);
        Assert.Null(foodItem.PersonId);

        var totals = OutgoingTotals.For(month.Items);
        Assert.Equal(new OutgoingTotals(710m, 190m, 520m, 0m), totals);
    }

    [Fact]
    public async Task Show_all_includes_outgoings_not_started_or_already_ended()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var food = await CategoryIdAsync(householdId, "Food & household");
        var future = Outgoing.Create(householdId, Details("Gym", food, new DateOnly(2027, 1, 1)), 30m, PaymentSchedule.Monthly, "Start");
        var ended = Outgoing.Create(householdId, Details("Old phone", food, new DateOnly(2025, 1, 1)), 20m, PaymentSchedule.Monthly, "Start");
        ended.End(new DateOnly(2026, 6, 1), "Cancelled");
        await ExecuteDbContextAsync(async db =>
        {
            db.Outgoings.AddRange(future, ended);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient(signedInAs: ownerId);

        var usual = (await client.GetFromJsonAsync<OutgoingMonthResponse>(Url(OutgoingRoutes.ForMonth(householdId, 2026, 11))))!;
        var all = (await client.GetFromJsonAsync<OutgoingMonthResponse>(Url(OutgoingRoutes.ForMonth(householdId, 2026, 11, all: true))))!;

        Assert.Empty(usual.Items);
        Assert.Equal(new[] { "Gym", "Old phone" }, all.Items.Select(i => i.Name));
        Assert.Equal(new[] { "Starts 1 Jan 2027" }, all.Items[0].Notes);
        Assert.Equal(new[] { "Ended from 1 Jun 2026" }, all.Items[1].Notes);
        Assert.All(all.Items, i => Assert.Equal(0m, i.MonthlyAmount));
    }

    [Fact]
    public async Task A_year_adds_up_the_12_monthly_budget_amounts_and_includes_paid_from()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var housing = await CategoryIdAsync(householdId, "Housing");
        var leisure = await CategoryIdAsync(householdId, "Leisure & memberships");

        var mortgage = Outgoing.Create(householdId, Details("Mortgage", housing, new DateOnly(2026, 1, 1)) with { PaidFrom = "Joint bills" }, 900m, PaymentSchedule.Monthly, "Start");
        mortgage.PauseFor(new DateOnly(2026, 11, 1), payments: 1, "Payment holiday");
        var councilTax = Outgoing.Create(householdId, Details("Council tax", housing, new DateOnly(2026, 4, 1)), 150m, PaymentSchedule.Monthly, "Start");
        councilTax.ChangeAmount(new DateOnly(2026, 12, 1), 180m, PaymentSchedule.Monthly, "New band");
        var gym = Outgoing.Create(householdId, Details("Gym", leisure, new DateOnly(2027, 1, 1)), 30m, PaymentSchedule.Monthly, "Start");
        var ended = Outgoing.Create(householdId, Details("Old phone", leisure, new DateOnly(2025, 1, 1)), 20m, PaymentSchedule.Monthly, "Start");
        ended.End(new DateOnly(2026, 6, 1), "Cancelled");
        await ExecuteDbContextAsync(async db =>
        {
            db.Outgoings.AddRange(mortgage, councilTax, gym, ended);
            await db.SaveChangesAsync();
        });
        using var client = CreateClient(signedInAs: ownerId);

        var year = (await client.GetFromJsonAsync<OutgoingMonthResponse>(Url(OutgoingRoutes.ForMonth(householdId, 2026, 11, months: 12))))!;

        Assert.Equal((2026, 11, 12), (year.Year, year.Month, year.Months));
        Assert.Equal(new[] { "Council tax", "Mortgage", "Gym" }, year.Items.Select(i => i.Name));
        // November at £150, then 11 months at £180.
        Assert.Equal((2130m, 180m), (year.Items[0].MonthlyAmount, year.Items[0].Amount));
        Assert.Equal(new[] { "Rises to £180.00 a month from 1 Dec 2026" }, year.Items[0].Notes);
        // November's payment is skipped, then 11 months.
        Assert.Equal((9900m, "Joint bills"), (year.Items[1].MonthlyAmount, year.Items[1].PaidFrom));
        // Starts in January, so 10 months.
        Assert.Equal((300m, (string?)null), (year.Items[2].MonthlyAmount, year.Items[2].PaidFrom));
        Assert.Equal(12330m, OutgoingTotals.For(year.Items).Total);
    }

    [Fact]
    public async Task Only_a_month_or_a_year_can_be_asked_for()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.GetAsync(Url(OutgoingRoutes.ForMonth(householdId, 2026, 11, months: 6)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_bad_month_is_rejected()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.GetAsync(Url(OutgoingRoutes.Outgoings(householdId) + "?month=November"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public Task Viewers_can_see_the_month() =>
        AssertMinimumRoleAsync(HttpMethod.Get, hh => OutgoingRoutes.ForMonth(hh, 2026, 11), HouseholdRole.Viewer);

    [Fact]
    public Task Other_households_cannot_see_the_month() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, hh => OutgoingRoutes.ForMonth(hh, 2026, 11));

    [Theory]
    [InlineData(typeof(OutgoingKind), typeof(OutgoingKindDto))]
    [InlineData(typeof(OutgoingPriority), typeof(OutgoingPriorityDto))]
    [InlineData(typeof(PaymentUnit), typeof(PaymentUnitDto))]
    [InlineData(typeof(PaymentMethod), typeof(PaymentMethodDto))]
    [InlineData(typeof(AlertPeriod), typeof(AlertPeriodDto))]
    public void Shared_enums_match_the_domain(Type domain, Type shared)
    {
        Assert.Equal(Enum.GetNames(domain), Enum.GetNames(shared));
        Assert.Equal(Enum.GetValuesAsUnderlyingType(domain).Cast<int>().ToArray(), Enum.GetValuesAsUnderlyingType(shared).Cast<int>().ToArray());
    }

    private static OutgoingDetails Details(string name, Guid categoryId, DateOnly firstPayment) => new()
    {
        Name = name,
        CategoryId = categoryId,
        FirstPaymentDate = firstPayment,
    };

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
