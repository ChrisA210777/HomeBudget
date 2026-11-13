using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Budget.Domain.Households;
using Budget.Domain.Income;
using Budget.Domain.Outgoings;
using Budget.Server.Features.DataExport;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.DataExport;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.DataExport;

/// <summary>GET /api/households/{id}/export (BUD-65).</summary>
[Collection(ApiCollection.Name)]
public class ExportApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task The_core_export_has_whats_current_or_future_and_nothing_ended()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        await AddDataAsync(householdId);
        using var client = CreateClient(signedInAs: ownerId);

        var export = await ExportAsync(client, householdId, history: false, audit: false);

        Assert.Equal((ExportFormat.FormatName, ExportFormat.CurrentSchemaVersion), (export.Format, export.SchemaVersion));
        Assert.Equal(("My Household", "GBP"), (export.Household.Name, export.Household.CurrencyCode));
        Assert.Equal(("Chris Austin", "Admin", true), (export.Members.Single().DisplayName, export.Members.Single().Role, export.Members.Single().IsOwner));
        var me = Assert.Single(export.People);
        Assert.Equal(export.Members.Single().Email, me.LinkedMemberEmail);
        Assert.Equal(OutgoingCategory.DefaultNames, export.Categories.Select(c => c.Name));
        Assert.Equal("home", export.Categories[0].Icon);

        // The ended outgoing is left out; the mortgage keeps today's amount (with its original date) and the future rise.
        var mortgage = Assert.Single(export.Outgoings);
        Assert.Equal("Mortgage", mortgage.Name);
        Assert.Equal(new[] { (Today.AddMonths(-3), 950m), (Today.AddMonths(6), 1050m) }, mortgage.AmountChanges.Select(c => (c.EffectiveFrom, c.Amount)));
        Assert.Equal("Month", mortgage.AmountChanges[0].Unit);
        // The past pause is left out; the coming one stays.
        Assert.Equal(new[] { "Holiday ahead" }, mortgage.StatusPeriods.Select(p => p.Comment));
        Assert.Empty(mortgage.Corrections);

        var salary = Assert.Single(export.Incomes);
        Assert.Equal(new[] { 3000m }, salary.AmountChanges.Select(c => c.Amount));
        Assert.Equal(me.Id, salary.PersonId);

        Assert.Null(mortgage.RecordedBy);
        Assert.Null(mortgage.RecordedAt);
    }

    [Fact]
    public async Task With_history_everything_is_included()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        await AddDataAsync(householdId);
        using var client = CreateClient(signedInAs: ownerId);

        var export = await ExportAsync(client, householdId, history: true, audit: false);

        Assert.Equal(new[] { "Gym", "Mortgage" }, export.Outgoings.Select(o => o.Name));
        var mortgage = export.Outgoings.Single(o => o.Name == "Mortgage");
        Assert.Equal(3, mortgage.AmountChanges.Count);
        Assert.Equal(2, mortgage.StatusPeriods.Count);
        var correction = Assert.Single(mortgage.Corrections);
        Assert.Equal(("AmountChange", "Edited", "Typo"), (correction.EntryType, correction.Action, correction.Reason));
        Assert.Equal(new[] { 2500m, 3000m }, export.Incomes.Single().AmountChanges.Select(c => c.Amount));
    }

    [Fact]
    public async Task With_audit_details_each_item_says_who_recorded_it_and_when()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        using var client = CreateClient(signedInAs: ownerId);

        var export = await ExportAsync(client, householdId, history: false, audit: true);

        var person = Assert.Single(export.People);
        Assert.NotNull(person.RecordedAt);
        Assert.NotNull(person.ChangedAt);
    }

    [Fact]
    public async Task The_file_is_named_for_the_household_and_day_and_reads_back_cleanly()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        await AddDataAsync(householdId);
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.GetAsync(new Uri(DataRoutes.Export(householdId, includeHistory: true, includeAudit: true), UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"home-budget-my-household-{DateTime.UtcNow:yyyy-MM-dd}.json", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        var read = ExportReader.Read(await response.Content.ReadAsByteArrayAsync());
        Assert.True(read.Succeeded, string.Join(Environment.NewLine, read.Errors));
    }

    [Fact]
    public async Task Nothing_about_logins_is_ever_included()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        var json = await client.GetStringAsync(new Uri(DataRoutes.Export(householdId, includeHistory: true, includeAudit: true), UriKind.Relative));

        Assert.DoesNotContain(ownerId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft", json, StringComparison.Ordinal);
    }

    [Fact]
    public Task Only_admins_can_export() =>
        AssertMinimumRoleAsync(HttpMethod.Get, h => DataRoutes.Export(h, false, false), HouseholdRole.Admin);

    [Fact]
    public Task Other_households_cant_export() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, h => DataRoutes.Export(h, false, false));

    private static async Task<HouseholdExport> ExportAsync(HttpClient client, Guid householdId, bool history, bool audit)
    {
        var bytes = await client.GetByteArrayAsync(new Uri(DataRoutes.Export(householdId, history, audit), UriKind.Relative));
        return JsonSerializer.Deserialize<HouseholdExport>(bytes, ExportFormat.JsonOptions)!;
    }

    /// <summary>
    /// A salary (with a past rise), a mortgage (a past and a future price, a corrected price, a past and a coming pause)
    /// and a gym membership that has ended.
    /// </summary>
    private Task AddDataAsync(Guid householdId) =>
        ExecuteDbContextAsync(async db =>
        {
            var categories = await db.OutgoingCategories.IgnoreQueryFilters().Where(c => c.HouseholdId == householdId).ToListAsync();
            var housing = categories.Single(c => c.Name == "Housing");
            var leisure = categories.Single(c => c.Name == "Leisure & memberships");
            var person = await db.People.IgnoreQueryFilters().SingleAsync(p => p.HouseholdId == householdId);

            var salary = IncomeStream.Create(householdId, person.Id, "Salary", 2500m, Today.AddYears(-1), "Starting amount");
            salary.ChangeAmount(Today.AddMonths(-2), 3000m, "Pay rise");

            var mortgageStart = new DateOnly(Today.AddMonths(-6).Year, Today.AddMonths(-6).Month, 1);
            var mortgage = Outgoing.Create(householdId, new OutgoingDetails { Name = "Mortgage", CategoryId = housing.Id, FirstPaymentDate = mortgageStart }, 900m, PaymentSchedule.Monthly, "Starting amount");
            var wrong = mortgage.ChangeAmount(Today.AddMonths(-3), 9500m, PaymentSchedule.Monthly, "Rise");
            mortgage.CorrectAmountChange(wrong.Id, Today.AddMonths(-3), 950m, PaymentSchedule.Monthly, "Rise", "Typo");
            mortgage.ChangeAmount(Today.AddMonths(6), 1050m, PaymentSchedule.Monthly, "New fixed rate");
            mortgage.PauseFor(mortgageStart.AddMonths(1), 1, "Holiday past");
            mortgage.PauseFor(mortgageStart.AddMonths(8), 1, "Holiday ahead");

            var gym = Outgoing.Create(householdId, new OutgoingDetails { Name = "Gym", CategoryId = leisure.Id, FirstPaymentDate = Today.AddMonths(-8) }, 30m, PaymentSchedule.Monthly, "Starting amount");
            gym.End(Today.AddMonths(-1), "Cancelled");

            db.IncomeStreams.Add(salary);
            db.Outgoings.AddRange(mortgage, gym);
            await db.SaveChangesAsync();
        });
}
