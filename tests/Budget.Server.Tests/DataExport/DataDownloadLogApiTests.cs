using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Account;
using Budget.Shared.DataExport;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.DataExport;

/// <summary>The data download log (BUD-141): GET /api/households/{id}/export/downloads.</summary>
[Collection(ApiCollection.Name)]
public class DataDownloadLogApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Every_download_is_logged_newest_first_with_what_was_downloaded()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        using var client = CreateClient(signedInAs: ownerId);

        _ = await client.GetByteArrayAsync(Url(DataRoutes.Export(householdId, includeHistory: false, includeAudit: false)));
        _ = await client.GetByteArrayAsync(Url(DataRoutes.ExportCsv(householdId, includeHistory: true, includeAudit: true)));

        var downloads = await DownloadsAsync(client, householdId);

        Assert.Equal(2, downloads.Count);
        Assert.Equal(("Csv", true, true), (downloads[0].Format, downloads[0].IncludesHistory, downloads[0].IncludesAudit));
        Assert.Equal(("Json", false, false), (downloads[1].Format, downloads[1].IncludesHistory, downloads[1].IncludesAudit));
        Assert.All(downloads, d => Assert.Equal(("Chris Austin", true), (d.Who, d.IsYou)));
        Assert.True(downloads[0].At >= downloads[1].At);
    }

    [Fact]
    public async Task Another_admins_download_shows_their_name()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        var janeId = await AddMemberAsync(householdId, HouseholdRole.Admin, "Jane Austin");
        using (var jane = CreateClient(signedInAs: janeId))
        {
            _ = await jane.GetByteArrayAsync(Url(DataRoutes.Export(householdId, includeHistory: false, includeAudit: false)));
        }

        using var client = CreateClient(signedInAs: ownerId);
        var download = Assert.Single(await DownloadsAsync(client, householdId));

        Assert.Equal(("Jane Austin", false), (download.Who, download.IsYou));
    }

    [Fact]
    public async Task Downloads_by_a_deleted_account_show_a_former_member()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Chris Austin");
        var janeId = await AddMemberAsync(householdId, HouseholdRole.Admin, "Jane Austin");
        using (var jane = CreateClient(signedInAs: janeId))
        {
            _ = await jane.GetByteArrayAsync(Url(DataRoutes.Export(householdId, includeHistory: false, includeAudit: false)));
            using var deleted = await jane.PostAsJsonAsync(Url(AccountRoutes.DeleteAccount), new DeleteAccountRequest { Confirmation = "DELETE" });
            deleted.EnsureSuccessStatusCode();
        }

        using var client = CreateClient(signedInAs: ownerId);
        var download = Assert.Single(await DownloadsAsync(client, householdId));

        Assert.Equal(("A former member", false), (download.Who, download.IsYou));
        await ExecuteDbContextAsync(async db =>
            Assert.False(await db.DataDownloads.IgnoreQueryFilters().AnyAsync(d => d.UserId == janeId)));
    }

    [Fact]
    public async Task Downloads_older_than_twelve_months_are_not_shown()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        await ExecuteDbContextAsync(async db =>
        {
            db.DataDownloads.Add(DataDownload.Record(householdId, ownerId, DateTimeOffset.UtcNow.AddDays(-400), DataDownloadFormat.Json, false, false));
            db.DataDownloads.Add(DataDownload.Record(householdId, ownerId, DateTimeOffset.UtcNow.AddDays(-30), DataDownloadFormat.Csv, false, false));
            await db.SaveChangesAsync();
        });
        using var client = CreateClient(signedInAs: ownerId);

        var download = Assert.Single(await DownloadsAsync(client, householdId));

        Assert.Equal("Csv", download.Format);
    }

    [Fact]
    public async Task Members_who_arent_admins_cant_download_so_nothing_is_logged()
    {
        var (_, householdId) = await CreateHouseholdOwnerAsync();
        var viewerId = await AddMemberAsync(householdId, HouseholdRole.Viewer);
        using var viewer = CreateClient(signedInAs: viewerId);

        using var response = await viewer.GetAsync(Url(DataRoutes.Export(householdId, includeHistory: false, includeAudit: false)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await ExecuteDbContextAsync(async db =>
            Assert.False(await db.DataDownloads.IgnoreQueryFilters().AnyAsync(d => d.HouseholdId == householdId)));
    }

    [Fact]
    public Task Only_admins_can_see_the_log() =>
        AssertMinimumRoleAsync(HttpMethod.Get, DataDownloadRoutes.List, HouseholdRole.Admin);

    [Fact]
    public Task Other_households_cant_see_the_log() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, DataDownloadRoutes.List);

    private static async Task<IReadOnlyList<DataDownloadSummary>> DownloadsAsync(HttpClient client, Guid householdId) =>
        (await client.GetFromJsonAsync<List<DataDownloadSummary>>(Url(DataDownloadRoutes.List(householdId))))!;

    private static Uri Url(string path) => new(path, UriKind.Relative);
}
