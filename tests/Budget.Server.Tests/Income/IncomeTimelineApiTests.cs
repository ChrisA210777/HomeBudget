using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.Budgeting;
using Budget.Shared.Income;
using Budget.Shared.Outgoings;
using Budget.Shared.People;
using Microsoft.AspNetCore.Http;

namespace Budget.Server.Tests.Income;

[Collection(ApiCollection.Name)]
public class IncomeTimelineApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly DateOnly January = new(2026, 1, 1);

    [Fact]
    public async Task Four_weekly_income_like_child_benefit_is_averaged_into_the_month()
    {
        var (client, householdId, personId) = await OwnerAsync("Chris");
        using var response = await client.PostAsJsonAsync(Url(IncomeRoutes.IncomeStreams(householdId)), new CreateIncomeStreamRequest
        {
            Name = "Child Benefit", PersonId = personId, Amount = 102.20m, Every = 4, Unit = PaymentUnitDto.Week, StartsOn = new DateOnly(2026, 1, 5),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<IncomeStreamDetails>())!;

        var first = Assert.Single(created.AmountChanges);
        Assert.Equal((102.20m, 4, PaymentUnitDto.Week, 110.72m), (first.Amount, first.Every, first.Unit, first.MonthlyAmount));
        Assert.Equal((102.20m, 4, PaymentUnitDto.Week), (created.ReceivedAmount, created.Every, created.Unit));

        var november = await MonthAsync(client, householdId, 2026, 11);
        var item = Assert.Single(november.Items);
        Assert.Equal((110.72m, 110.72m), (item.AmountForMonth, item.UsualAmount!.Value));
        Assert.Equal((102.20m, 4, PaymentUnitDto.Week), (item.ReceivedAmount!.Value, item.Every, item.Unit));

        var budget = (await client.GetFromJsonAsync<BudgetMonthResponse>(Url(BudgetRoutes.Month(householdId, 2026, 11))))!;
        Assert.Equal(110.72m, budget.Income);
    }

    [Fact]
    public async Task A_change_of_schedule_is_recorded_with_the_amount()
    {
        var (client, householdId, personId) = await OwnerAsync("Chris");
        var created = await CreateAsync(client, householdId, personId, "Freelance", 1000m, January);

        var updated = await SendAsync<IncomeStreamDetails>(client, HttpMethod.Post, IncomeRoutes.AmountChanges(householdId, created.Id), new SaveAmountChangeRequest
        {
            Amount = 300m, Every = 1, Unit = PaymentUnitDto.Week, EffectiveFrom = new DateOnly(2026, 6, 1), Comment = "Paid weekly now", Version = created.Version,
        });
        var june = await MonthAsync(client, householdId, 2026, 6);

        Assert.Equal(1300m, june.Items.Single().AmountForMonth);
        Assert.Contains(updated.AmountChanges, c => c.Unit == PaymentUnitDto.Week && c.MonthlyAmount == 1300m);
        using var invalid = await client.PostAsJsonAsync(Url(IncomeRoutes.AmountChanges(householdId, created.Id)), new SaveAmountChangeRequest
        {
            Amount = 300m, Every = 0, EffectiveFrom = new DateOnly(2026, 7, 1), Comment = "Bad", Version = updated.Version,
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task New_income_shows_its_monthly_amount_from_when_it_starts()
    {
        var (client, householdId, personId) = await OwnerAsync("Chris");
        var created = await CreateAsync(client, householdId, personId, "Chris – salary", 3000m, January);

        Assert.Equal("Chris – salary", created.Name);
        Assert.Equal("Chris", created.PersonName);
        var first = Assert.Single(created.AmountChanges);
        Assert.Equal("Starting amount", first.Comment);
        Assert.True(first.IsFirst);
        Assert.Equal("Chris", first.RecordedBy);

        var december = await MonthAsync(client, householdId, 2025, 12);
        var march = await MonthAsync(client, householdId, 2026, 3);

        Assert.Empty(december.Items);
        var item = Assert.Single(march.Items);
        Assert.Equal(3000m, item.AmountForMonth);
        Assert.Equal(3000m, item.UsualAmount);
        Assert.Empty(item.Notes);
        Assert.Equal(3000m, march.Total);
        Assert.Equal("GBP", march.CurrencyCode);
    }

    [Fact]
    public async Task A_pay_rise_part_way_through_a_month_is_pro_rata_and_explained()
    {
        var (client, householdId, personId) = await OwnerAsync("Chris");
        var created = await CreateAsync(client, householdId, personId, "Salary", 3000m, January);

        var updated = await SendAsync<IncomeStreamDetails>(client, HttpMethod.Post, IncomeRoutes.AmountChanges(householdId, created.Id), new SaveAmountChangeRequest
        {
            Amount = 3300m,
            EffectiveFrom = new DateOnly(2026, 11, 16),
            Comment = "10% pay rise",
            Version = created.Version,
        });

        Assert.NotEqual(created.Version, updated.Version);
        var november = Assert.Single((await MonthAsync(client, householdId, 2026, 11)).Items);
        Assert.Equal(3150m, november.AmountForMonth);
        Assert.Equal(3300m, november.UsualAmount);
        Assert.Equal(new[] { "Changes to £3,300.00 a month from 16 Nov 2026" }, november.Notes);
        Assert.Equal(3300m, Assert.Single((await MonthAsync(client, householdId, 2026, 12)).Items).AmountForMonth);

        // A year from November adds up its 12 months (BUD-80): £3,150 in November, then 11 × £3,300.
        var year = (await client.GetFromJsonAsync<IncomeMonthResponse>(Url(IncomeRoutes.ForMonth(householdId, 2026, 11, months: 12))))!;
        Assert.Equal((2026, 11, 12), (year.Year, year.Month, year.Months));
        Assert.Equal(39450m, Assert.Single(year.Items).AmountForMonth);
        Assert.Equal(39450m, year.Total);
        Assert.Equal(new[] { "Changes to £3,300.00 a month from 16 Nov 2026" }, year.Items[0].Notes);
    }

    [Fact]
    public async Task Only_a_month_or_a_year_can_be_asked_for()
    {
        var (client, householdId, _) = await OwnerAsync("Chris");

        using var response = await client.GetAsync(Url(IncomeRoutes.ForMonth(householdId, 2026, 11, months: 6)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Leave_with_reduced_pay_is_pro_rata_and_explained()
    {
        var (client, householdId, personId) = await OwnerAsync("Chris");
        var created = await CreateAsync(client, householdId, personId, "Salary", 3000m, January);

        await SendAsync<IncomeStreamDetails>(client, HttpMethod.Post, IncomeRoutes.StatusPeriods(householdId, created.Id), new SaveStatusPeriodRequest
        {
            Reason = IncomeStatusReasonDto.SickLeave,
            StartDate = new DateOnly(2026, 11, 16),
            EndDate = null,
            ReducedMonthlyAmount = 470m,
            Comment = "Statutory sick pay",
            Version = created.Version,
        });

        var november = Assert.Single((await MonthAsync(client, householdId, 2026, 11)).Items);
        Assert.Equal(1735m, november.AmountForMonth);
        Assert.Equal(3000m, november.UsualAmount);
        Assert.Equal(new[] { "Sick leave 16 Nov 2026 – until further notice (£470.00 a month)" }, november.Notes);
    }

    [Fact]
    public async Task Ended_income_is_hidden_from_later_months_unless_showing_all()
    {
        var (client, householdId, personId) = await OwnerAsync("Chris");
        var created = await CreateAsync(client, householdId, personId, "Old job", 3000m, January);
        await SendAsync<IncomeStreamDetails>(client, HttpMethod.Post, IncomeRoutes.StatusPeriods(householdId, created.Id), new SaveStatusPeriodRequest
        {
            Reason = IncomeStatusReasonDto.Ended,
            StartDate = new DateOnly(2026, 6, 1),
            Comment = "Left Acme",
            Version = created.Version,
        });

        var may = Assert.Single((await MonthAsync(client, householdId, 2026, 5)).Items);
        var june = await MonthAsync(client, householdId, 2026, 6);
        var juneAll = await client.GetFromJsonAsync<IncomeMonthResponse>(Url(IncomeRoutes.ForMonth(householdId, 2026, 6, all: true)));

        Assert.Equal(3000m, may.AmountForMonth);
        Assert.Empty(june.Items);
        var ended = Assert.Single(juneAll!.Items);
        Assert.Equal(0m, ended.AmountForMonth);
        Assert.Equal(new DateOnly(2026, 6, 1), ended.EndsOn);
        Assert.Equal(0m, juneAll.Total);
    }

    [Fact]
    public async Task Month_total_adds_up_every_income()
    {
        var (client, householdId, chrisId) = await OwnerAsync("Chris");
        var janeId = await AddPersonAsync(client, householdId, "Jane");
        await CreateAsync(client, householdId, chrisId, "Chris – salary", 3000.10m, January);
        await CreateAsync(client, householdId, janeId, "Jane – contracts", 1850.25m, January);

        var month = await MonthAsync(client, householdId, 2026, 2);

        Assert.Equal(new[] { "Chris", "Jane" }, month.Items.Select(i => i.PersonName));
        Assert.Equal(4850.35m, month.Total);
    }

    [Fact]
    public async Task Corrections_keep_the_original_and_the_reason()
    {
        var (client, householdId, personId) = await OwnerAsync("Chris");
        var created = await CreateAsync(client, householdId, personId, "Salary", 3000m, January);
        var withRise = await SendAsync<IncomeStreamDetails>(client, HttpMethod.Post, IncomeRoutes.AmountChanges(householdId, created.Id), new SaveAmountChangeRequest
        {
            Amount = 3200m, EffectiveFrom = new DateOnly(2026, 4, 1), Comment = "Pay rise", Version = created.Version,
        });
        var rise = withRise.AmountChanges.Single(c => !c.IsFirst);

        var corrected = await SendAsync<IncomeStreamDetails>(client, HttpMethod.Put, IncomeRoutes.AmountChange(householdId, created.Id, rise.Id), new SaveAmountChangeRequest
        {
            Amount = 3100m, EffectiveFrom = new DateOnly(2026, 4, 1), Comment = "Pay rise", CorrectionReason = "Typed the wrong figure", Version = withRise.Version,
        });
        var deleted = await SendAsync<IncomeStreamDetails>(client, HttpMethod.Delete, IncomeRoutes.AmountChange(householdId, created.Id, rise.Id), new DeleteEntryRequest
        {
            Reason = "Rise was postponed", Version = corrected.Version,
        });

        Assert.Single(deleted.AmountChanges);
        Assert.Equal(new[] { "Deleted", "Edited" }, deleted.Corrections.Select(c => c.Action));
        var edit = deleted.Corrections.Single(c => c.Action == "Edited");
        Assert.Contains("3,200.00", edit.Before, StringComparison.Ordinal);
        Assert.Contains("3,100.00", edit.After, StringComparison.Ordinal);
        Assert.Equal("Typed the wrong figure", edit.Reason);
        Assert.Equal("Chris", edit.CorrectedBy);
    }

    [Fact]
    public async Task Timeline_rules_are_reported_against_the_right_field()
    {
        var (client, householdId, personId) = await OwnerAsync("Chris");
        var created = await CreateAsync(client, householdId, personId, "Salary", 3000m, January);
        var first = created.AmountChanges.Single();

        var duplicateDate = await ProblemAsync(client, HttpMethod.Post, IncomeRoutes.AmountChanges(householdId, created.Id), new SaveAmountChangeRequest
        {
            Amount = 3100m, EffectiveFrom = January, Comment = "Same day", Version = created.Version,
        });
        var deleteFirst = await ProblemAsync(client, HttpMethod.Delete, IncomeRoutes.AmountChange(householdId, created.Id, first.Id), new DeleteEntryRequest
        {
            Reason = "Oops", Version = created.Version,
        });
        var withLeave = await SendAsync<IncomeStreamDetails>(client, HttpMethod.Post, IncomeRoutes.StatusPeriods(householdId, created.Id), new SaveStatusPeriodRequest
        {
            Reason = IncomeStatusReasonDto.Holiday, StartDate = new DateOnly(2026, 8, 1), EndDate = new DateOnly(2026, 8, 14), Comment = "Unpaid holiday", Version = created.Version,
        });
        var overlap = await ProblemAsync(client, HttpMethod.Post, IncomeRoutes.StatusPeriods(householdId, created.Id), new SaveStatusPeriodRequest
        {
            Reason = IncomeStatusReasonDto.SickLeave, StartDate = new DateOnly(2026, 8, 10), Comment = "Sick", Version = withLeave.Version,
        });

        Assert.Contains("EffectiveFrom", duplicateDate.Errors.Keys);
        Assert.Contains("Reason", deleteFirst.Errors.Keys);
        Assert.Contains("StartDate", overlap.Errors.Keys);
    }

    [Fact]
    public async Task Saving_over_someone_elses_change_is_rejected()
    {
        var (client, householdId, personId) = await OwnerAsync("Chris");
        var created = await CreateAsync(client, householdId, personId, "Salary", 3000m, January);
        var request = new SaveAmountChangeRequest { Amount = 3100m, EffectiveFrom = new DateOnly(2026, 4, 1), Comment = "Rise", Version = created.Version };

        using var first = await client.PostAsJsonAsync(Url(IncomeRoutes.AmountChanges(householdId, created.Id)), request);
        using var second = await client.PostAsJsonAsync(Url(IncomeRoutes.AmountChanges(householdId, created.Id)), request with { EffectiveFrom = new DateOnly(2026, 5, 1) });
        using var noVersion = await client.PostAsJsonAsync(Url(IncomeRoutes.AmountChanges(householdId, created.Id)), request with { Version = null });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);
        var details = await client.GetFromJsonAsync<IncomeStreamDetails>(Url(IncomeRoutes.IncomeStream(householdId, created.Id)));
        Assert.Equal(2, details!.AmountChanges.Count);
    }

    [Fact]
    public async Task Income_can_be_renamed_and_moved_to_another_person_in_the_household()
    {
        var (client, householdId, chrisId) = await OwnerAsync("Chris");
        var janeId = await AddPersonAsync(client, householdId, "Jane");
        var created = await CreateAsync(client, householdId, chrisId, "Contracts", 1850m, January);

        var updated = await SendAsync<IncomeStreamDetails>(client, HttpMethod.Put, IncomeRoutes.IncomeStream(householdId, created.Id), new UpdateIncomeStreamRequest
        {
            Name = "Jane – contracts", PersonId = janeId, Version = created.Version,
        });

        Assert.Equal("Jane – contracts", updated.Name);
        Assert.Equal("Jane", updated.PersonName);
    }

    [Fact]
    public async Task Income_must_belong_to_someone_in_this_household()
    {
        var (otherClient, otherHousehold, otherPerson) = await OwnerAsync("Other");
        var (client, householdId, _) = await OwnerAsync("Chris");
        Assert.NotEqual(otherHousehold, householdId);
        otherClient.Dispose();

        var problem = await ProblemAsync(client, HttpMethod.Post, IncomeRoutes.IncomeStreams(householdId), new CreateIncomeStreamRequest
        {
            Name = "Sneaky", PersonId = otherPerson, Amount = 100m, StartsOn = January,
        });

        Assert.Contains("PersonId", problem.Errors.Keys);
    }

    [Fact]
    public async Task Deleted_income_is_gone()
    {
        var (client, householdId, personId) = await OwnerAsync("Chris");
        var created = await CreateAsync(client, householdId, personId, "Salary", 3000m, January);

        using var delete = await client.DeleteAsync(Url(IncomeRoutes.IncomeStream(householdId, created.Id)));
        using var get = await client.GetAsync(Url(IncomeRoutes.IncomeStream(householdId, created.Id)));

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Another_households_income_cannot_be_reached_through_your_own_household()
    {
        var (otherClient, otherHousehold, otherPerson) = await OwnerAsync("Other");
        var theirs = await CreateAsync(otherClient, otherHousehold, otherPerson, "Theirs", 3000m, January);
        var (client, householdId, _) = await OwnerAsync("Chris");

        using var get = await client.GetAsync(Url(IncomeRoutes.IncomeStream(householdId, theirs.Id)));
        using var change = await client.PostAsJsonAsync(Url(IncomeRoutes.AmountChanges(householdId, theirs.Id)), new SaveAmountChangeRequest
        {
            Amount = 1m, EffectiveFrom = new DateOnly(2026, 3, 1), Comment = "Hijack", Version = theirs.Version,
        });
        using var delete = await client.DeleteAsync(Url(IncomeRoutes.IncomeStream(householdId, theirs.Id)));
        var month = await MonthAsync(client, householdId, 2026, 3);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, change.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Empty(month.Items);
        Assert.Single((await otherClient.GetFromJsonAsync<IncomeStreamDetails>(Url(IncomeRoutes.IncomeStream(otherHousehold, theirs.Id))))!.AmountChanges);
    }

    // Isolation and roles for every endpoint (see Features/README.md).

    public static TheoryData<string> Endpoints => new()
    {
        "month", "create", "details", "update", "delete",
        "add-amount", "correct-amount", "delete-amount", "add-period", "correct-period", "delete-period",
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public Task Other_households_cannot_use_any_income_endpoint(string endpoint)
    {
        var (method, _, body) = Endpoint(endpoint, Guid.NewGuid(), Guid.NewGuid());
        return AssertOtherHouseholdsCannotAccessAsync(method, householdId => Endpoint(endpoint, householdId, Guid.NewGuid()).Url, body);
    }

    [Fact]
    public Task Anyone_can_see_a_months_income() =>
        AssertMinimumRoleAsync(HttpMethod.Get, householdId => IncomeRoutes.ForMonth(householdId, 2026, 1), HouseholdRole.Viewer);

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("add-amount")]
    [InlineData("correct-amount")]
    [InlineData("delete-amount")]
    [InlineData("add-period")]
    [InlineData("correct-period")]
    [InlineData("delete-period")]
    [InlineData("create")]
    public async Task Viewers_cannot_change_income(string endpoint)
    {
        var (ownerClient, householdId, personId) = await OwnerAsync("Owner");
        var created = await CreateAsync(ownerClient, householdId, personId, "Salary", 3000m, January);
        var viewerId = await AddMemberAsync(householdId, HouseholdRole.Viewer);
        using var viewer = CreateClient(signedInAs: viewerId, followRedirects: false);
        var (method, url, body) = Endpoint(endpoint, householdId, created.Id, created.AmountChanges.Single().Id, created.Version, personId);

        using var request = new HttpRequestMessage(method, Url(url)) { Content = body is null ? null : JsonContent.Create(body) };
        using var response = await viewer.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Users_can_add_income()
    {
        var (ownerClient, householdId, personId) = await OwnerAsync("Owner");
        ownerClient.Dispose();
        var userId = await AddMemberAsync(householdId, HouseholdRole.User);
        using var user = CreateClient(signedInAs: userId);

        var created = await CreateAsync(user, householdId, personId, "Added by a User", 100m, January);

        Assert.Equal("Household Member", created.AmountChanges.Single().RecordedBy);
    }

    private static (HttpMethod Method, string Url, object? Body) Endpoint(
        string name, Guid householdId, Guid streamId, Guid? entryId = null, string? version = null, Guid? personId = null)
    {
        var entry = entryId ?? Guid.NewGuid();
        var v = version ?? "AAAAAAAAAAA=";
        var amount = new SaveAmountChangeRequest { Amount = 1m, EffectiveFrom = new DateOnly(2026, 3, 1), Comment = "c", CorrectionReason = "r", Version = v };
        var period = new SaveStatusPeriodRequest { Reason = IncomeStatusReasonDto.Holiday, StartDate = new DateOnly(2026, 3, 1), Comment = "c", CorrectionReason = "r", Version = v };
        var delete = new DeleteEntryRequest { Reason = "r", Version = v };

        return name switch
        {
            "month" => (HttpMethod.Get, IncomeRoutes.ForMonth(householdId, 2026, 1), null),
            "create" => (HttpMethod.Post, IncomeRoutes.IncomeStreams(householdId),
                new CreateIncomeStreamRequest { Name = "n", PersonId = personId ?? Guid.NewGuid(), Amount = 1m, StartsOn = new DateOnly(2026, 1, 1) }),
            "details" => (HttpMethod.Get, IncomeRoutes.IncomeStream(householdId, streamId), null),
            "update" => (HttpMethod.Put, IncomeRoutes.IncomeStream(householdId, streamId), new UpdateIncomeStreamRequest { Name = "n", PersonId = personId ?? Guid.NewGuid(), Version = v }),
            "delete" => (HttpMethod.Delete, IncomeRoutes.IncomeStream(householdId, streamId), null),
            "add-amount" => (HttpMethod.Post, IncomeRoutes.AmountChanges(householdId, streamId), amount),
            "correct-amount" => (HttpMethod.Put, IncomeRoutes.AmountChange(householdId, streamId, entry), amount),
            "delete-amount" => (HttpMethod.Delete, IncomeRoutes.AmountChange(householdId, streamId, entry), delete),
            "add-period" => (HttpMethod.Post, IncomeRoutes.StatusPeriods(householdId, streamId), period),
            "correct-period" => (HttpMethod.Put, IncomeRoutes.StatusPeriod(householdId, streamId, entry), period),
            "delete-period" => (HttpMethod.Delete, IncomeRoutes.StatusPeriod(householdId, streamId, entry), delete),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };
    }

    // ---- Helpers ----

    private async Task<(HttpClient Client, Guid HouseholdId, Guid PersonId)> OwnerAsync(string name)
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync(name);
        var client = CreateClient(signedInAs: ownerId);
        var people = await client.GetFromJsonAsync<List<PersonSummary>>(Url(PeopleRoutes.People(householdId)));
        return (client, householdId, people!.Single(p => p.IsYou).Id);
    }

    private static async Task<Guid> AddPersonAsync(HttpClient client, Guid householdId, string name)
    {
        using var response = await client.PostAsJsonAsync(Url(PeopleRoutes.People(householdId)), new SavePersonRequest { DisplayName = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PersonSummary>())!.Id;
    }

    private static async Task<IncomeStreamDetails> CreateAsync(HttpClient client, Guid householdId, Guid personId, string name, decimal amount, DateOnly startsOn)
    {
        using var response = await client.PostAsJsonAsync(Url(IncomeRoutes.IncomeStreams(householdId)), new CreateIncomeStreamRequest
        {
            Name = name, PersonId = personId, Amount = amount, StartsOn = startsOn,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IncomeStreamDetails>())!;
    }

    private static async Task<IncomeMonthResponse> MonthAsync(HttpClient client, Guid householdId, int year, int month) =>
        (await client.GetFromJsonAsync<IncomeMonthResponse>(Url(IncomeRoutes.ForMonth(householdId, year, month))))!;

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string url, object body)
    {
        using var request = new HttpRequestMessage(method, Url(url)) { Content = JsonContent.Create(body) };
        using var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, $"{method} {url} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<HttpValidationProblemDetails> ProblemAsync(HttpClient client, HttpMethod method, string url, object body)
    {
        using var request = new HttpRequestMessage(method, Url(url)) { Content = JsonContent.Create(body) };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>())!;
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
