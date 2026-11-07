using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Account;
using Budget.Shared.DataExport;
using Budget.Shared.Outgoings;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.DataExport;

/// <summary>POST /api/households/{id}/import/new-household (BUD-63).</summary>
[Collection(ApiCollection.Name)]
public class ImportNewHouseholdApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly byte[] GoldenFile = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ImportCompatibility", "v1", "full-history-and-audit.json"));

    [Fact]
    public async Task The_file_becomes_a_new_household_owned_by_you_and_it_becomes_active()
    {
        var (userId, ownHouseholdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        using var client = CreateClient(signedInAs: userId);

        var result = await ImportAsync(client, ownHouseholdId, GoldenFile);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => $"{e.Path}: {e.Message}")));
        Assert.Equal("The Austins", result.HouseholdName);
        var newId = result.HouseholdId!.Value;
        Assert.NotEqual(ownHouseholdId, newId);

        var me = await client.GetFromJsonAsync<CurrentUserResponse>(new Uri(AccountRoutes.CurrentUser, UriKind.Relative));
        Assert.Equal(newId, me!.LastActiveHouseholdId);
        var membership = me.Households.Single(h => h.HouseholdId == newId);
        Assert.Equal(("Admin", true), (membership.Role, membership.IsOwner));

        await ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(1, await db.HouseholdMembers.CountAsync(m => m.HouseholdId == newId));
            Assert.Equal(3, await db.People.IgnoreQueryFilters().CountAsync(p => p.HouseholdId == newId));
            Assert.Equal(3, await db.OutgoingCategories.IgnoreQueryFilters().CountAsync(c => c.HouseholdId == newId));
            Assert.Equal(2, await db.IncomeStreams.IgnoreQueryFilters().CountAsync(i => i.HouseholdId == newId));
            Assert.Equal(3, await db.Outgoings.IgnoreQueryFilters().CountAsync(o => o.HouseholdId == newId));

            // Your own household is untouched.
            Assert.Equal(16, await db.OutgoingCategories.IgnoreQueryFilters().CountAsync(c => c.HouseholdId == ownHouseholdId));
        });
    }

    [Fact]
    public async Task Who_first_recorded_each_item_is_kept_and_exported_again()
    {
        var (userId, ownHouseholdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: userId);
        var newId = (await ImportAsync(client, ownHouseholdId, GoldenFile)).HouseholdId!.Value;

        var export = JsonSerializer.Deserialize<HouseholdExport>(
            await client.GetByteArrayAsync(new Uri(DataRoutes.Export(newId, includeHistory: true, includeAudit: true), UriKind.Relative)), ExportFormat.JsonOptions)!;

        var salary = export.Incomes.Single(i => i.Name == "Salary");
        Assert.Equal(("Chris Austin", new DateTimeOffset(2026, 1, 2, 9, 10, 0, TimeSpan.Zero)), (salary.RecordedBy, salary.RecordedAt));
        Assert.Equal("Chris Austin", salary.Corrections.Single().RecordedBy);
        Assert.Equal("Jane Austin", export.Categories.Single(c => c.Name == "Hobbies").RecordedBy);
    }

    [Fact]
    public async Task Exporting_importing_and_exporting_again_gives_the_same_data()
    {
        var (userId, ownHouseholdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: userId);
        var first = (await ImportAsync(client, ownHouseholdId, GoldenFile)).HouseholdId!.Value;
        var exported = await client.GetByteArrayAsync(new Uri(DataRoutes.Export(first, includeHistory: true, includeAudit: true), UriKind.Relative));

        var second = (await ImportAsync(client, first, exported)).HouseholdId!.Value;
        var again = await client.GetByteArrayAsync(new Uri(DataRoutes.Export(second, includeHistory: true, includeAudit: true), UriKind.Relative));

        Assert.True(JsonNode.DeepEquals(Comparable(exported), Comparable(again)));
    }

    [Fact]
    public async Task You_can_choose_your_name_and_which_person_is_you()
    {
        var (userId, ownHouseholdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: userId);
        var sam = Guid.Parse("0192f0a0-0000-7000-8000-000000000003");

        var result = await ImportAsync(client, ownHouseholdId, GoldenFile, "Austins restored", sam);

        Assert.Equal("Austins restored", result.HouseholdName);
        await ExecuteDbContextAsync(async db =>
        {
            var linked = await db.People.IgnoreQueryFilters().SingleAsync(p => p.HouseholdId == result.HouseholdId && p.LinkedUserId == userId);
            Assert.Equal("Sam", linked.DisplayName);
        });
    }

    [Fact]
    public async Task The_person_with_your_email_is_linked_to_you()
    {
        var (userId, ownHouseholdId) = await CreateHouseholdOwnerAsync();
        const string email = "jane.austin@example.com";
        await ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.Email = email;
            await db.SaveChangesAsync();
        });
        using var client = CreateClient(signedInAs: userId);
        var file = Encoding.UTF8.GetString(GoldenFile).Replace("jane@example.com", email, StringComparison.Ordinal);

        var preview = await PostAsync<ImportPreviewResponse>(client, DataRoutes.Preview(ownHouseholdId), Encoding.UTF8.GetBytes(file));
        var result = await ImportAsync(client, ownHouseholdId, Encoding.UTF8.GetBytes(file));

        Assert.Equal("Jane", preview.People.Single(p => p.IsYou).DisplayName);
        await ExecuteDbContextAsync(async db =>
            Assert.Equal("Jane", (await db.People.IgnoreQueryFilters().SingleAsync(p => p.HouseholdId == result.HouseholdId && p.LinkedUserId == userId)).DisplayName));
    }

    [Fact]
    public async Task A_bad_file_creates_nothing()
    {
        var (userId, ownHouseholdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: userId);
        var document = JsonNode.Parse(GoldenFile)!;
        document["outgoings"]![0]!["statusPeriods"]![0]!["startDate"] = "2026-12-15";
        var name = $"Should not exist {Guid.NewGuid():N}";
        document["household"]!["name"] = name;

        var result = await ImportAsync(client, ownHouseholdId, Encoding.UTF8.GetBytes(document.ToJsonString()));

        Assert.False(result.Succeeded);
        Assert.Equal("outgoings[0].statusPeriods[0]", Assert.Single(result.Errors).Path);
        await ExecuteDbContextAsync(async db => Assert.False(await db.Households.AnyAsync(h => h.Name == name)));
    }

    [Fact]
    public async Task Only_GBP_households_can_be_imported_for_now()
    {
        var (userId, ownHouseholdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: userId);
        var file = Encoding.UTF8.GetString(GoldenFile).Replace("\"GBP\"", "\"EUR\"", StringComparison.Ordinal);

        var result = await ImportAsync(client, ownHouseholdId, Encoding.UTF8.GetBytes(file));

        Assert.Equal("household.currencyCode", Assert.Single(result.Errors).Path);
    }

    [Fact]
    public Task Only_admins_can_import() =>
        AssertMinimumRoleAsync(HttpMethod.Post, h => DataRoutes.ImportAsNewHousehold(h, null, null), HouseholdRole.Admin, () => JsonNode.Parse("{}"));

    [Fact]
    public Task Other_households_cant_import() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Post, h => DataRoutes.ImportAsNewHousehold(h, null, null), JsonNode.Parse("{}"));

    /// <summary>The file without what changes on every import: ids, when it was exported, and when it was last changed.</summary>
    private static JsonNode Comparable(byte[] export)
    {
        var node = JsonNode.Parse(export)!;
        node.AsObject().Remove("exportedAt");
        node.AsObject().Remove("members");
        node["household"]!.AsObject().Remove("name");
        Strip(node);
        return node;

        static void Strip(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    foreach (var key in new[] { "id", "personId", "categoryId", "entryId", "changedBy", "changedAt", "linkedMemberEmail" })
                    {
                        o.Remove(key);
                    }

                    foreach (var child in o.Select(p => p.Value).ToList())
                    {
                        Strip(child);
                    }

                    break;
                case JsonArray a:
                    foreach (var child in a)
                    {
                        Strip(child);
                    }

                    break;
            }
        }
    }

    private static Task<ImportResultResponse> ImportAsync(HttpClient client, Guid householdId, byte[] file, string? name = null, Guid? me = null) =>
        PostAsync<ImportResultResponse>(client, DataRoutes.ImportAsNewHousehold(householdId, name, me), file);

    private static async Task<T> PostAsync<T>(HttpClient client, string url, byte[] file)
    {
        using var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.PostAsync(new Uri(url, UriKind.Relative), content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
