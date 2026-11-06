using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.Outgoings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.Outgoings;

[Collection(ApiCollection.Name)]
public class OutgoingDetailApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // A monthly bill that started on the 1st of a month two years ago, so today's month has a past and a future payment day.
    private static readonly DateOnly FirstPayment = new DateOnly(Today.Year, Today.Month, 1).AddYears(-2);

    [Fact]
    public async Task Details_show_everything_with_the_next_payment_and_upcoming_dates()
    {
        var (client, householdId, outgoingId) = await CreateOutgoingAsync();

        var details = await GetAsync(client, householdId, outgoingId);

        Assert.Equal(("Car insurance", "Insurance", OutgoingKindDto.Bill), (details.Name, details.CategoryName, details.Kind));
        Assert.Equal((40m, 1, PaymentUnitDto.Month, 40m), (details.Amount, details.Every, details.Unit, details.MonthlyAmount));
        Assert.Equal(FirstPayment, details.FirstPaymentDate);
        Assert.Equal(new DateOnly(Today.Year, Today.Month, 1).AddMonths(Today.Day == 1 ? 0 : 1), details.NextPayment);
        Assert.Equal(12, details.UpcomingPayments.Count);
        Assert.Equal(details.NextPayment, details.UpcomingPayments[0]);
        Assert.Equal("https://example.com/", details.Website);
        Assert.Null(details.Status);
        var first = Assert.Single(details.AmountChanges);
        Assert.True(first.IsFirst);
        Assert.False(string.IsNullOrEmpty(details.Version));
    }

    [Fact]
    public async Task Amounts_can_be_changed_corrected_and_deleted_with_reasons()
    {
        var (client, householdId, outgoingId) = await CreateOutgoingAsync();
        var details = await GetAsync(client, householdId, outgoingId);
        var nextYear = Today.AddYears(1);

        using var added = await client.PostAsJsonAsync(Url(OutgoingRoutes.AmountChanges(householdId, outgoingId)), new SaveOutgoingAmountRequest
        {
            Amount = 540m, Every = 1, Unit = PaymentUnitDto.Year, EffectiveFrom = nextYear, Comment = "Paying annually", Version = details.Version,
        });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        details = (await added.Content.ReadFromJsonAsync<OutgoingDetailsResponse>())!;
        var change = details.AmountChanges.Single(c => !c.IsFirst);
        Assert.True(change.IsUpcoming);
        Assert.Equal(45m, change.MonthlyAmount);

        using var noReason = await client.PutAsJsonAsync(Url(OutgoingRoutes.AmountChange(householdId, outgoingId, change.Id)), new SaveOutgoingAmountRequest
        {
            Amount = 560m, Every = 1, Unit = PaymentUnitDto.Year, EffectiveFrom = nextYear, Comment = "Paying annually", Version = details.Version,
        });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        using var corrected = await client.PutAsJsonAsync(Url(OutgoingRoutes.AmountChange(householdId, outgoingId, change.Id)), new SaveOutgoingAmountRequest
        {
            Amount = 560m, Every = 1, Unit = PaymentUnitDto.Year, EffectiveFrom = nextYear, Comment = "Paying annually", CorrectionReason = "Typo", Version = details.Version,
        });
        details = (await corrected.Content.ReadFromJsonAsync<OutgoingDetailsResponse>())!;

        using var deleted = await SendAsync(client, HttpMethod.Delete, OutgoingRoutes.AmountChange(householdId, outgoingId, change.Id),
            new DeleteOutgoingEntryRequest { Reason = "Changed our minds", Version = details.Version });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        details = (await deleted.Content.ReadFromJsonAsync<OutgoingDetailsResponse>())!;

        Assert.Single(details.AmountChanges);
        Assert.Equal(new[] { "Deleted", "Edited" }, details.Corrections.Select(c => c.Action));
        Assert.Contains("£540.00 a year", details.Corrections[1].Before, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pause_skips_whole_payments_from_a_payment_date()
    {
        var (client, householdId, outgoingId) = await CreateOutgoingAsync();
        var details = await GetAsync(client, householdId, outgoingId);
        var firstSkipped = details.UpcomingPayments[1];

        using var midMonth = await client.PostAsJsonAsync(Url(OutgoingRoutes.StatusPeriods(householdId, outgoingId)), new SaveOutgoingStatusRequest
        {
            Reason = OutgoingStatusReasonDto.Paused, StartDate = firstSkipped.AddDays(14), SkipPayments = 1, Comment = "Holiday", Version = details.Version,
        });
        Assert.Equal(HttpStatusCode.BadRequest, midMonth.StatusCode);
        Assert.Contains("StartDate", (await midMonth.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);

        using var paused = await client.PostAsJsonAsync(Url(OutgoingRoutes.StatusPeriods(householdId, outgoingId)), new SaveOutgoingStatusRequest
        {
            Reason = OutgoingStatusReasonDto.Paused, StartDate = firstSkipped, SkipPayments = 3, Comment = "Payment holiday", Version = details.Version,
        });
        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        details = (await paused.Content.ReadFromJsonAsync<OutgoingDetailsResponse>())!;

        var pause = Assert.Single(details.StatusPeriods);
        Assert.Equal((firstSkipped, firstSkipped.AddMonths(3), 3), (pause.StartDate, pause.ResumesOn!.Value, pause.SkippedPayments!.Value));
        Assert.True(pause.IsUpcoming);
        Assert.Equal(details.UpcomingPayments[0], details.NextPayment);

        var month = (await client.GetFromJsonAsync<OutgoingMonthResponse>(Url(OutgoingRoutes.ForMonth(householdId, firstSkipped.Year, firstSkipped.Month))))!;
        Assert.Equal(0m, month.Items.Single().MonthlyAmount);
    }

    [Fact]
    public async Task A_current_pause_or_ending_shows_as_the_status()
    {
        var (client, householdId, outgoingId) = await CreateOutgoingAsync();
        var details = await GetAsync(client, householdId, outgoingId);
        var lastPayment = new DateOnly(Today.Year, Today.Month, 1);

        using var paused = await client.PostAsJsonAsync(Url(OutgoingRoutes.StatusPeriods(householdId, outgoingId)), new SaveOutgoingStatusRequest
        {
            Reason = OutgoingStatusReasonDto.Paused, StartDate = lastPayment, Comment = "Suspended", Version = details.Version,
        });
        details = (await paused.Content.ReadFromJsonAsync<OutgoingDetailsResponse>())!;
        Assert.Equal("Paused until further notice", details.Status);
        Assert.Null(details.NextPayment);

        var pause = details.StatusPeriods.Single();
        using var corrected = await client.PutAsJsonAsync(Url(OutgoingRoutes.StatusPeriod(householdId, outgoingId, pause.Id)), new SaveOutgoingStatusRequest
        {
            Reason = OutgoingStatusReasonDto.Paused, StartDate = lastPayment, SkipPayments = 6, Comment = "Suspended", CorrectionReason = "Now know when", Version = details.Version,
        });
        details = (await corrected.Content.ReadFromJsonAsync<OutgoingDetailsResponse>())!;
        Assert.StartsWith("Paused until ", details.Status, StringComparison.Ordinal);
        Assert.Equal(6, details.StatusPeriods.Single().SkippedPayments);

        using var ended = await client.PostAsJsonAsync(Url(OutgoingRoutes.StatusPeriods(householdId, outgoingId)), new SaveOutgoingStatusRequest
        {
            Reason = OutgoingStatusReasonDto.Ended, StartDate = lastPayment.AddMonths(6), Comment = "Cancelled", Version = details.Version,
        });
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        details = (await ended.Content.ReadFromJsonAsync<OutgoingDetailsResponse>())!;
        Assert.Equal(lastPayment.AddMonths(6), details.EndsOn);
    }

    [Fact]
    public async Task Details_can_be_edited_keeping_a_hidden_category_but_not_choosing_one()
    {
        var (client, householdId, outgoingId) = await CreateOutgoingAsync();
        var details = await GetAsync(client, householdId, outgoingId);
        var categories = (await client.GetFromJsonAsync<List<OutgoingCategorySummary>>(Url(OutgoingCategoryRoutes.Categories(householdId))))!;
        var pets = categories.Single(c => c.Name == "Pets");
        (await client.PutAsJsonAsync(Url(OutgoingCategoryRoutes.Category(householdId, details.CategoryId)), new UpdateCategoryRequest { Name = "Insurance", IsHidden = true })).Dispose();
        (await client.PutAsJsonAsync(Url(OutgoingCategoryRoutes.Category(householdId, pets.Id)), new UpdateCategoryRequest { Name = "Pets", IsHidden = true })).Dispose();

        using var kept = await client.PutAsJsonAsync(Url(OutgoingRoutes.Outgoing(householdId, outgoingId)), Edit(details) with { Name = "Car insurance (Ford)" });
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        details = (await kept.Content.ReadFromJsonAsync<OutgoingDetailsResponse>())!;
        Assert.Equal("Car insurance (Ford)", details.Name);
        Assert.Equal(FirstPayment, details.FirstPaymentDate);

        using var hidden = await client.PutAsJsonAsync(Url(OutgoingRoutes.Outgoing(householdId, outgoingId)), Edit(details) with { CategoryId = pets.Id });
        Assert.Equal(HttpStatusCode.BadRequest, hidden.StatusCode);
    }

    [Fact]
    public async Task Saving_over_someone_elses_change_is_refused()
    {
        var (client, householdId, outgoingId) = await CreateOutgoingAsync();
        var stale = await GetAsync(client, householdId, outgoingId);
        (await client.PutAsJsonAsync(Url(OutgoingRoutes.Outgoing(householdId, outgoingId)), Edit(stale) with { Name = "First" })).Dispose();

        using var second = await client.PutAsJsonAsync(Url(OutgoingRoutes.Outgoing(householdId, outgoingId)), Edit(stale) with { Name = "Second" });
        using var amount = await client.PostAsJsonAsync(Url(OutgoingRoutes.AmountChanges(householdId, outgoingId)), new SaveOutgoingAmountRequest
        {
            Amount = 50m, EffectiveFrom = Today.AddMonths(2), Comment = "Rise", Version = stale.Version,
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, amount.StatusCode);
        Assert.Equal("First", (await GetAsync(client, householdId, outgoingId)).Name);
    }

    [Fact]
    public async Task An_outgoing_can_be_deleted()
    {
        var (client, householdId, outgoingId) = await CreateOutgoingAsync();

        using var deleted = await client.DeleteAsync(Url(OutgoingRoutes.Outgoing(householdId, outgoingId)));
        using var after = await client.GetAsync(Url(OutgoingRoutes.Outgoing(householdId, outgoingId)));

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
        await ExecuteDbContextAsync(async db => Assert.False(await db.Outgoings.IgnoreQueryFilters().AnyAsync(o => o.Id == outgoingId)));
    }

    [Fact]
    public async Task Viewers_can_see_but_not_change()
    {
        var (_, householdId, outgoingId) = await CreateOutgoingAsync();
        var viewerId = await AddMemberAsync(householdId, HouseholdRole.Viewer);
        using var viewer = CreateClient(signedInAs: viewerId);

        var details = await GetAsync(viewer, householdId, outgoingId);
        using var edit = await viewer.PutAsJsonAsync(Url(OutgoingRoutes.Outgoing(householdId, outgoingId)), Edit(details));
        using var amount = await viewer.PostAsJsonAsync(Url(OutgoingRoutes.AmountChanges(householdId, outgoingId)), new SaveOutgoingAmountRequest());
        using var pause = await viewer.PostAsJsonAsync(Url(OutgoingRoutes.StatusPeriods(householdId, outgoingId)), new SaveOutgoingStatusRequest());
        using var delete = await viewer.DeleteAsync(Url(OutgoingRoutes.Outgoing(householdId, outgoingId)));

        Assert.All(new[] { edit, amount, pause, delete }, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }

    [Fact]
    public async Task Other_households_outgoings_are_not_found()
    {
        var (_, _, otherOutgoing) = await CreateOutgoingAsync();
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var get = await client.GetAsync(Url(OutgoingRoutes.Outgoing(householdId, otherOutgoing)));
        using var delete = await client.DeleteAsync(Url(OutgoingRoutes.Outgoing(householdId, otherOutgoing)));

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, hh => OutgoingRoutes.Outgoing(hh, otherOutgoing));
    }

    private async Task<(HttpClient Client, Guid HouseholdId, Guid OutgoingId)> CreateOutgoingAsync()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var client = CreateClient(signedInAs: ownerId);
        var categories = (await client.GetFromJsonAsync<List<OutgoingCategorySummary>>(Url(OutgoingCategoryRoutes.Categories(householdId))))!;
        using var created = await client.PostAsJsonAsync(Url(OutgoingRoutes.Outgoings(householdId)), new CreateOutgoingRequest
        {
            Kind = OutgoingKindDto.Bill,
            Name = "Car insurance",
            CategoryId = categories.Single(c => c.Name == "Insurance").Id,
            Priority = OutgoingPriorityDto.Essential,
            Amount = 40m,
            FirstPaymentDate = FirstPayment,
            Website = "example.com",
        });
        created.EnsureSuccessStatusCode();
        return (client, householdId, (await created.Content.ReadFromJsonAsync<OutgoingCreatedResponse>())!.Id);
    }

    private static UpdateOutgoingRequest Edit(OutgoingDetailsResponse details) => new()
    {
        Kind = details.Kind,
        Name = details.Name,
        CategoryId = details.CategoryId,
        Priority = details.Priority,
        PersonId = details.PersonId,
        Website = details.Website,
        Version = details.Version,
    };

    private static async Task<OutgoingDetailsResponse> GetAsync(HttpClient client, Guid householdId, Guid outgoingId) =>
        (await client.GetFromJsonAsync<OutgoingDetailsResponse>(Url(OutgoingRoutes.Outgoing(householdId, outgoingId))))!;

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object body)
    {
        using var request = new HttpRequestMessage(method, Url(url)) { Content = JsonContent.Create(body) };
        return await client.SendAsync(request);
    }

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
