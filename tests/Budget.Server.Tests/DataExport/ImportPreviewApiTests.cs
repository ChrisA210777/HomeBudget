using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.DataExport;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.DataExport;

/// <summary>POST /api/households/{id}/import/preview (BUD-67).</summary>
[Collection(ApiCollection.Name)]
public class ImportPreviewApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly string GoldenFile = Path.Combine(AppContext.BaseDirectory, "ImportCompatibility", "v1", "full-history-and-audit.json");

    [Fact]
    public async Task A_good_file_is_summarised_and_can_be_imported()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        var preview = await PreviewAsync(client, householdId, await File.ReadAllBytesAsync(GoldenFile));

        Assert.True(preview.CanImport, string.Join("; ", preview.Errors.Select(e => $"{e.Path}: {e.Message}")));
        Assert.Empty(preview.Errors);
        Assert.Equal(("The Austins", "GBP", 1), (preview.HouseholdName, preview.CurrencyCode, preview.FileVersion));
        Assert.True(preview.IncludesHistory);
        Assert.True(preview.IncludesAudit);
        Assert.Equal(new ImportCounts { People = 3, Categories = 3, Incomes = 2, Outgoings = 3, HistoryEntries = 12 }, preview.Counts);
        Assert.Equal(new[] { "Chris Austin", "Jane Austin" }, preview.Members.Select(m => m.DisplayName));
    }

    [Fact]
    public async Task Nothing_is_saved()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        await PreviewAsync(client, householdId, await File.ReadAllBytesAsync(GoldenFile));

        await ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(1, await db.People.IgnoreQueryFilters().CountAsync(p => p.HouseholdId == householdId));
            Assert.Equal(0, await db.Outgoings.IgnoreQueryFilters().CountAsync(o => o.HouseholdId == householdId));
            // No household was made for the caller (other tests share the database, so not checked by name).
            Assert.Equal(1, await db.HouseholdMembers.CountAsync(m => m.UserId == ownerId));
        });
    }

    [Fact]
    public async Task Domain_rules_are_checked_and_say_where_the_problem_is()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);
        var document = JsonNode.Parse(await File.ReadAllTextAsync(GoldenFile))!;

        // The mortgage is paid on the 1st, so a pause can't start on the 15th.
        document["outgoings"]![0]!["statusPeriods"]![0]!["startDate"] = "2026-12-15";

        var preview = await PreviewAsync(client, householdId, Encoding.UTF8.GetBytes(document.ToJsonString()));

        Assert.False(preview.CanImport);
        var error = Assert.Single(preview.Errors);
        Assert.Equal("outgoings[0].statusPeriods[0]", error.Path);
        Assert.Equal("The Austins", preview.HouseholdName);
    }

    [Fact]
    public async Task Unreadable_files_are_explained()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        var notExport = await PreviewAsync(client, householdId, """{ "hello": "world" }"""u8.ToArray());
        var tooBig = await PreviewAsync(client, householdId, new byte[ExportFormat.MaxFileBytes + 1]);

        Assert.False(notExport.CanImport);
        Assert.Equal("This isn't a Home Budget export file.", Assert.Single(notExport.Errors).Message);
        Assert.Null(notExport.HouseholdName);
        Assert.Contains("too big", Assert.Single(tooBig.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_households_own_export_can_be_imported_with_or_without_history()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        foreach (var history in new[] { false, true })
        {
            var file = await client.GetByteArrayAsync(new Uri(DataRoutes.Export(householdId, history, includeAudit: true), UriKind.Relative));
            var preview = await PreviewAsync(client, householdId, file);
            Assert.True(preview.CanImport, string.Join("; ", preview.Errors.Select(e => $"{e.Path}: {e.Message}")));
        }
    }

    [Fact]
    public Task Only_admins_can_check_files() =>
        AssertMinimumRoleAsync(HttpMethod.Post, DataRoutes.Preview, HouseholdRole.Admin, () => JsonNode.Parse("{}"));

    [Fact]
    public Task Other_households_cant_check_files() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Post, DataRoutes.Preview, JsonNode.Parse("{}"));

    private static async Task<ImportPreviewResponse> PreviewAsync(HttpClient client, Guid householdId, byte[] file)
    {
        using var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.PostAsync(new Uri(DataRoutes.Preview(householdId), UriKind.Relative), content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportPreviewResponse>())!;
    }
}
