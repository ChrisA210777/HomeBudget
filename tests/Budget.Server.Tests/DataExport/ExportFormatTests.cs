using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Budget.Server.Features.DataExport;
using Budget.Shared.DataExport;

namespace Budget.Server.Tests.DataExport;

/// <summary>The export format and its backward compatibility (BUD-64). These run on every PR.</summary>
public class ExportFormatTests
{
    private static readonly string CompatibilityFolder = Path.Combine(AppContext.BaseDirectory, "ImportCompatibility");

    public static TheoryData<string> GoldenFiles() =>
        [.. Directory.GetFiles(CompatibilityFolder, "*.json", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f))!.StartsWith('v'))
            .Select(f => Path.GetRelativePath(CompatibilityFolder, f))];

    [Theory]
    [MemberData(nameof(GoldenFiles))]
    public void Every_file_any_released_version_wrote_still_reads(string goldenFile)
    {
        var result = ExportReader.Read(File.ReadAllBytes(Path.Combine(CompatibilityFolder, goldenFile)));

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(ExportFormat.CurrentSchemaVersion, result.Document!.SchemaVersion);

        // And every domain rule is met, so it would import.
        var (plan, problems) = ImportPlanner.Plan(result.Document, Guid.NewGuid());
        Assert.True(plan is not null, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void There_is_a_golden_file_for_every_released_version()
    {
        for (var version = 1; version <= ExportFormat.CurrentSchemaVersion; version++)
        {
            var folder = Path.Combine(CompatibilityFolder, $"v{version}");
            Assert.True(Directory.Exists(folder) && Directory.GetFiles(folder, "*.json").Length > 0, $"Add a golden file under tests/ImportCompatibility/v{version}/.");
        }
    }

    [Fact]
    public void There_is_one_upgrader_for_each_older_version()
    {
        Assert.Equal(Enumerable.Range(1, ExportFormat.CurrentSchemaVersion - 1), ExportReader.Upgraders.Select(u => u.FromVersion));
    }

    /// <summary>
    /// Any change to the contracts must bump the schema version (with an upgrader and a golden file). This compares
    /// the contracts' JSON Schema with the snapshot stored for the current version.
    /// </summary>
    [Fact]
    public void The_format_hasnt_changed_without_a_new_version()
    {
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(ExportFormat.JsonOptions, typeof(HouseholdExport))
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var snapshot = Path.Combine(CompatibilityFolder, $"schema-v{ExportFormat.CurrentSchemaVersion}.json");

        Assert.True(File.Exists(snapshot),
            $"There's no schema snapshot for version {ExportFormat.CurrentSchemaVersion}. Save this as tests/ImportCompatibility/schema-v{ExportFormat.CurrentSchemaVersion}.json:{Environment.NewLine}{schema}");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(snapshot)), JsonNode.Parse(schema)),
            "The export contracts have changed. Bump ExportFormat.CurrentSchemaVersion, add an upgrader and a golden file, and save the new schema snapshot. " +
            $"The schema is now:{Environment.NewLine}{schema}");
    }

    [Fact]
    public void A_file_that_isnt_json_is_refused()
    {
        var result = ExportReader.Read("not json at all"u8.ToArray());

        Assert.False(result.Succeeded);
        Assert.Contains("isn't a valid JSON file", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "hello": "world" }""")]
    [InlineData("""{ "format": "something-else", "schemaVersion": 1 }""")]
    [InlineData("""[1, 2, 3]""")]
    public void A_file_that_isnt_a_home_budget_export_is_refused(string json)
    {
        var result = ExportReader.Read(Encoding.UTF8.GetBytes(json));

        Assert.Equal("This isn't a Home Budget export file.", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void A_file_from_a_newer_version_is_refused_with_a_clear_message()
    {
        var newer = Golden().ToJsonString().Replace("\"schemaVersion\":1", $"\"schemaVersion\":{ExportFormat.CurrentSchemaVersion + 1}", StringComparison.Ordinal);

        var result = ExportReader.Read(Encoding.UTF8.GetBytes(newer));

        Assert.StartsWith("This file was exported by a newer version of Home Budget", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
        Assert.Equal(ExportFormat.CurrentSchemaVersion + 1, result.FileVersion);
    }

    [Fact]
    public void A_file_over_10_MB_is_refused()
    {
        var result = ExportReader.Read(new byte[ExportFormat.MaxFileBytes + 1]);

        Assert.Contains("too big", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Problems_say_where_they_are()
    {
        var document = Golden();
        document["outgoings"]![1]!["amountChanges"]![0]!["amount"] = 0;
        document["outgoings"]![0]!["categoryId"] = Guid.NewGuid().ToString();
        document["incomes"]![0]!["amountChanges"]![0]!["unit"] = "Fortnight";
        document["categories"]![0]!["icon"] = "rocket";

        var result = ExportReader.Read(Encoding.UTF8.GetBytes(document.ToJsonString()));

        Assert.False(result.Succeeded);
        var problems = result.Errors.Select(e => e.ToString()).ToList();
        Assert.Contains("outgoings[1].amountChanges[0].amount: must be more than 0.", problems);
        Assert.Contains("outgoings[0].categoryId: doesn't match any of the categories in the file.", problems);
        Assert.Contains(problems, p => p.StartsWith("incomes[0].amountChanges[0].unit: \"Fortnight\" isn't one of", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("categories[0].icon:", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_properties_and_wrong_kinds_of_value_are_refused_with_where_they_are()
    {
        var misspelt = Golden();
        misspelt["outgoings"]![0]!.AsObject()["amont"] = 5;
        var wrongKind = Golden();
        wrongKind["incomes"]![0]!["amountChanges"]![0]!["amount"] = "lots";

        var misspeltError = Assert.Single(ExportReader.Read(Encoding.UTF8.GetBytes(misspelt.ToJsonString())).Errors);
        var wrongKindError = Assert.Single(ExportReader.Read(Encoding.UTF8.GetBytes(wrongKind.ToJsonString())).Errors);

        Assert.StartsWith("outgoings[0]", misspeltError.Path, StringComparison.Ordinal);
        Assert.Equal("isn't part of the export format.", misspeltError.Message);
        Assert.Equal("incomes[0].amountChanges[0].amount", wrongKindError.Path);
    }

    [Theory]
    [InlineData("The Austins", "the-austins")]
    [InlineData("  Mum's budget ", "mums-budget")]
    [InlineData("Smith & Jones", "smith-jones")]
    [InlineData("££££", "household")]
    public void File_names_are_safe(string name, string expected)
    {
        Assert.Equal(expected, DataExportEndpoints.FileNamePart(name));
    }

    private static JsonObject Golden() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(CompatibilityFolder, "v1", "full-history-and-audit.json")))!.AsObject();
}
