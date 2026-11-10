using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Budget.Domain.Households;
using Budget.Server.Features.DataExport;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.DataExport;

namespace Budget.Server.Tests.DataExport;

/// <summary>The CSV export for Excel (BUD-66).</summary>
[Collection(ApiCollection.Name)]
public class CsvExportTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    private static HouseholdExport Golden() =>
        JsonSerializer.Deserialize<HouseholdExport>(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ImportCompatibility", "v1", "full-history-and-audit.json")), ExportFormat.JsonOptions)!;

    [Theory]
    [InlineData("Plain", "Plain")]
    [InlineData("Smith, John", "\"Smith, John\"")]
    [InlineData("Say \"hi\"", "\"Say \"\"hi\"\"\"")]
    [InlineData("=1+2", "'=1+2")]
    [InlineData("+44 7700 900000", "'+44 7700 900000")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    public void Text_is_quoted_when_needed_and_never_runs_as_a_formula(string value, string expected)
    {
        Assert.Equal(expected, CsvExporter.Cell(value));
    }

    [Fact]
    public void Values_are_written_plainly_for_any_locale()
    {
        Assert.Equal("2026-03-14", CsvExporter.Cell(new DateOnly(2026, 3, 14)));
        Assert.Equal("1234.50", CsvExporter.Cell(1234.5m));
        Assert.Equal("102.20", CsvExporter.Cell(102.2m));
        Assert.Equal("0.125", CsvExporter.Cell(0.125m));
        Assert.Equal("true", CsvExporter.Cell(true));
        Assert.Equal(string.Empty, CsvExporter.Cell(null));
        Assert.Equal("2026-11-06T17:30:00+00:00", CsvExporter.Cell(new DateTimeOffset(2026, 11, 6, 17, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Each_table_is_its_own_file_with_a_readme()
    {
        var files = Read(CsvExporter.Write(Golden()));

        Assert.Equal(
            new[]
            {
                "README.txt", "accounts.csv", "categories.csv", "corrections.csv", "household.csv", "income_amount_changes.csv", "income_status_periods.csv",
                "incomes.csv", "members.csv", "outgoing_amount_changes.csv", "outgoing_status_periods.csv", "outgoings.csv", "people.csv",
                "transfer_amount_changes.csv", "transfer_status_periods.csv", "transfers.csv",
            },
            files.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("Format version 1", files["README.txt"], StringComparison.Ordinal);
    }

    [Fact]
    public void Tables_are_raw_rows_joined_by_ids_with_a_bom_for_excel()
    {
        var export = Golden();
        var zip = CsvExporter.Write(export);
        var files = Read(zip);

        using (var archive = new ZipArchive(new MemoryStream(zip)))
        using (var stream = archive.GetEntry("household.csv")!.Open())
        {
            var bom = new byte[3];
            stream.ReadExactly(bom);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bom);
        }

        var incomes = Lines(files["income_amount_changes.csv"]);
        Assert.Equal("id,incomeId,effectiveFrom,amount,every,unit,comment,recordedBy,recordedAt,changedBy,changedAt", incomes[0]);
        Assert.StartsWith($"{export.Incomes[0].AmountChanges[0].Id},{export.Incomes[0].Id},2026-01-01,2800.00,1,Month,Starting amount,Chris Austin,", incomes[1], StringComparison.Ordinal);

        var outgoings = Lines(files["outgoings.csv"]);
        Assert.Equal(4, outgoings.Length);
        Assert.Contains(",https://example.com,0800 000 000,Fixed until March 2027,", outgoings[1], StringComparison.Ordinal);
        Assert.Equal("name,currencyCode,transfersEnabled,exportedAt,schemaVersion", Lines(files["household.csv"])[0]);
        Assert.Equal("The Austins,GBP,false,2026-11-06T17:30:00+00:00,1", Lines(files["household.csv"])[1]);
        Assert.Equal(new[] { "id,name,from,to,firstPaymentDate,notes,recordedBy,recordedAt,changedBy,changedAt" }, Lines(files["transfers.csv"]));
    }

    [Fact]
    public void Without_history_or_audit_there_are_no_corrections_or_audit_columns()
    {
        var export = Golden() with { Options = new ExportOptions() };

        var files = Read(CsvExporter.Write(export));

        Assert.False(files.ContainsKey("corrections.csv"));
        Assert.Equal("id,name,sortOrder,isHidden,icon", Lines(files["categories.csv"])[0]);
    }

    [Fact]
    public async Task Admins_download_a_zip_named_for_the_household()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        using var client = CreateClient(signedInAs: ownerId);

        using var response = await client.GetAsync(new Uri(DataRoutes.ExportCsv(householdId, includeHistory: true, includeAudit: false), UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"home-budget-my-household-{DateTime.UtcNow:yyyy-MM-dd}-csv.zip",
            response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var files = Read(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(17, Lines(files["categories.csv"]).Length);
    }

    [Fact]
    public Task Only_admins_can_download_csv() =>
        AssertMinimumRoleAsync(HttpMethod.Get, h => DataRoutes.ExportCsv(h, false, false), HouseholdRole.Admin);

    [Fact]
    public Task Other_households_cant_download_csv() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, h => DataRoutes.ExportCsv(h, false, false));

    private static Dictionary<string, string> Read(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip));
        return archive.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var reader = new StreamReader(e.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        });
    }

    private static string[] Lines(string csv) => csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
}
