using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Budget.Domain.Households;
using Budget.Domain.Outgoings;
using Budget.Domain.People;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.DataExport;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.DataExport;

/// <summary>POST /api/households/{id}/import/replace (BUD-62).</summary>
[Collection(ApiCollection.Name)]
public class ReplaceHouseholdDataApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly byte[] GoldenFile = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ImportCompatibility", "v1", "full-history-and-audit.json"));

    [Fact]
    public async Task The_households_data_is_swapped_for_the_files_and_members_stay()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        var memberId = await AddMemberAsync(householdId, HouseholdRole.User, "Jane Austin");
        await AddOldDataAsync(householdId, memberId);
        using var client = CreateClient(signedInAs: ownerId);

        var preview = await PostAsync<ImportPreviewResponse>(client, DataRoutes.Preview(householdId), GoldenFile);
        var result = await PostAsync<ImportResultResponse>(client, ReplaceRoutes.Replace(householdId, useFileName: false), GoldenFile);

        Assert.Equal(new ImportCounts { People = 1, Categories = 16, Incomes = 0, Outgoings = 1 }, preview.WouldDelete);
        Assert.Equal(new[] { "Chris", "JANE" }, preview.PeopleKept);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => $"{e.Path}: {e.Message}")));
        Assert.Equal((householdId, "My Household"), (result.HouseholdId, result.HouseholdName));

        await ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(2, await db.HouseholdMembers.CountAsync(m => m.HouseholdId == householdId));
            var people = await db.People.IgnoreQueryFilters().Where(p => p.HouseholdId == householdId).ToListAsync();

            // Chris and JANE are kept (linked to members), and the file's Chris and Jane became them (names match ignoring
            // case); Sam is new; Old friend went.
            Assert.Equal(new[] { "Chris", "JANE", "Sam" }, people.Select(p => p.DisplayName).Order(StringComparer.Ordinal));
            Assert.Equal(ownerId, people.Single(p => p.DisplayName == "Chris").LinkedUserId);
            Assert.Equal(memberId, people.Single(p => p.DisplayName == "JANE").LinkedUserId);

            var categories = await db.OutgoingCategories.IgnoreQueryFilters().Where(c => c.HouseholdId == householdId).Select(c => c.Name).ToListAsync();
            Assert.Equal(new[] { "Hobbies", "Housing", "Insurance" }, categories.Order(StringComparer.Ordinal));
            var outgoings = await db.Outgoings.IgnoreQueryFilters().Where(o => o.HouseholdId == householdId).Select(o => o.Name).ToListAsync();
            Assert.DoesNotContain("Old gym", outgoings);
            Assert.Equal(3, outgoings.Count);

            var salary = await db.IncomeStreams.IgnoreQueryFilters().SingleAsync(i => i.HouseholdId == householdId && i.Name == "Salary");
            Assert.Equal(people.Single(p => p.DisplayName == "Chris").Id, salary.PersonId);
        });
    }

    [Fact]
    public async Task The_household_can_take_the_files_name()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        var result = await PostAsync<ImportResultResponse>(client, ReplaceRoutes.Replace(householdId, useFileName: true), GoldenFile);

        Assert.Equal("The Austins", result.HouseholdName);
    }

    [Fact]
    public async Task A_bad_file_changes_nothing()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        await AddOldDataAsync(householdId, linkedUserId: null);
        using var client = CreateClient(signedInAs: ownerId);
        var document = JsonNode.Parse(GoldenFile)!;
        document["outgoings"]![0]!["statusPeriods"]![0]!["startDate"] = "2026-12-15";

        var result = await PostAsync<ImportResultResponse>(client, ReplaceRoutes.Replace(householdId, useFileName: true), Encoding.UTF8.GetBytes(document.ToJsonString()));

        Assert.False(result.Succeeded);
        await ExecuteDbContextAsync(async db =>
        {
            Assert.True(await db.Outgoings.IgnoreQueryFilters().AnyAsync(o => o.HouseholdId == householdId && o.Name == "Old gym"));
            Assert.Equal(16, await db.OutgoingCategories.IgnoreQueryFilters().CountAsync(c => c.HouseholdId == householdId));
            Assert.Equal("My Household", (await db.Households.SingleAsync(h => h.Id == householdId)).Name);
        });
    }

    [Fact]
    public async Task A_file_in_another_currency_is_refused()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        var file = Encoding.UTF8.GetString(GoldenFile).Replace("\"GBP\"", "\"EUR\"", StringComparison.Ordinal);

        var result = await PostAsync<ImportResultResponse>(client, ReplaceRoutes.Replace(householdId, false), Encoding.UTF8.GetBytes(file));

        Assert.Equal("household.currencyCode", Assert.Single(result.Errors).Path);
    }

    [Fact]
    public Task Only_admins_can_replace_data() =>
        AssertMinimumRoleAsync(HttpMethod.Post, h => ReplaceRoutes.Replace(h, false), HouseholdRole.Admin, () => JsonNode.Parse("{}"));

    [Fact]
    public Task Other_households_cant_replace_data() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Post, h => ReplaceRoutes.Replace(h, false), JsonNode.Parse("{}"));

    /// <summary>A person who doesn't sign in, a person for the member (if given) named Jane, and an outgoing.</summary>
    private Task AddOldDataAsync(Guid householdId, Guid? linkedUserId) =>
        ExecuteDbContextAsync(async db =>
        {
            db.People.Add(Person.Create(householdId, "Old friend"));
            if (linkedUserId is { } userId)
            {
                db.People.Add(Person.Create(householdId, "JANE", userId));
            }

            var housing = await db.OutgoingCategories.IgnoreQueryFilters().SingleAsync(c => c.HouseholdId == householdId && c.Name == "Housing");
            db.Outgoings.Add(Outgoing.Create(householdId, new OutgoingDetails { Name = "Old gym", CategoryId = housing.Id, FirstPaymentDate = new DateOnly(2026, 1, 1) }, 30m, PaymentSchedule.Monthly, "Starting amount"));
            await db.SaveChangesAsync();
        });

    private static async Task<T> PostAsync<T>(HttpClient client, string url, byte[] file)
    {
        using var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.PostAsync(new Uri(url, UriKind.Relative), content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
