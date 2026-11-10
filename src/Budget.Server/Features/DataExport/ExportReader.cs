using System.Text.Json;
using System.Text.Json.Nodes;
using Budget.Shared.DataExport;

namespace Budget.Server.Features.DataExport;

/// <summary>A problem with an import file, and where in the file it is (e.g. <c>outgoings[3].amountChanges[0].amount</c>).</summary>
public sealed record ExportProblem(string Path, string Message)
{
    public override string ToString() => string.IsNullOrEmpty(Path) ? Message : $"{Path}: {Message}";
}

/// <summary>The outcome of reading a file: the document as the current version, or why it couldn't be read.</summary>
public sealed record ExportReadResult(HouseholdExport? Document, IReadOnlyList<ExportProblem> Errors, IReadOnlyList<string> Warnings, int? FileVersion)
{
    public bool Succeeded => Document is not null && Errors.Count == 0;
}

/// <summary>
/// Upgrades a document from one schema version to the next, working on the JSON itself (BUD-64). Once released,
/// an upgrader is never changed or removed: any file the app has ever written must still import.
/// </summary>
public interface IExportUpgrader
{
    /// <summary>The version this upgrades from; it produces <c>FromVersion + 1</c>.</summary>
    int FromVersion { get; }

    void Upgrade(JsonObject document);
}

/// <summary>
/// Reads an export file of any released version (BUD-64): checks it's a Home Budget export, refuses newer versions,
/// upgrades older ones one version at a time, then reads and checks it as the current version. Nothing is saved.
/// </summary>
public static class ExportReader
{
    /// <summary>One per version before the current one, in order. None yet: version 1 is the first.</summary>
    public static IReadOnlyList<IExportUpgrader> Upgraders { get; } = [new ExportUpgraderV1(), new ExportUpgraderV2()];

    public static ExportReadResult Read(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.LongLength > ExportFormat.MaxFileBytes)
        {
            return Failed($"The file is too big. Exports are at most {ExportFormat.MaxFileBytes / (1024 * 1024)} MB.");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(file, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException ex)
        {
            return Failed($"This isn't a valid JSON file (line {ex.LineNumber + 1}).");
        }

        if (root is not JsonObject document
            || document["format"] is not JsonValue format
            || !format.TryGetValue<string>(out var formatName)
            || formatName != ExportFormat.FormatName)
        {
            return Failed("This isn't a Home Budget export file.");
        }

        if (document["schemaVersion"] is not JsonValue versionValue || !versionValue.TryGetValue<int>(out var version) || version < 1)
        {
            return Failed("The file doesn't say which version of the export format it uses.", "schemaVersion");
        }

        if (version > ExportFormat.CurrentSchemaVersion)
        {
            return new ExportReadResult(null, [new ExportProblem(string.Empty,
                $"This file was exported by a newer version of Home Budget (format version {version}). This version can read format versions up to {ExportFormat.CurrentSchemaVersion}.")],
                [], version);
        }

        var warnings = new List<string>();
        for (var from = version; from < ExportFormat.CurrentSchemaVersion; from++)
        {
            var upgrader = Upgraders.Single(u => u.FromVersion == from);
            upgrader.Upgrade(document);
            document["schemaVersion"] = from + 1;
        }

        if (version < ExportFormat.CurrentSchemaVersion)
        {
            warnings.Add($"This file is from an older version of Home Budget (format version {version}) and was upgraded.");
        }

        HouseholdExport? export;
        try
        {
            export = document.Deserialize<HouseholdExport>(ExportFormat.JsonOptions);
        }
        catch (JsonException ex)
        {
            return new ExportReadResult(null, [new ExportProblem(JsonPath(ex.Path), Describe(ex))], warnings, version);
        }

        if (export is null)
        {
            return Failed("The file is empty.");
        }

        var errors = ExportValidator.Validate(export);
        return new ExportReadResult(errors.Count == 0 ? export : null, errors, warnings, version);

        ExportReadResult Failed(string message, string path = "") => new(null, [new ExportProblem(path, message)], [], null);
    }

    /// <summary>"$.outgoings[3].amountChanges[0].amount" → "outgoings[3].amountChanges[0].amount".</summary>
    private static string JsonPath(string? path) => path is null ? string.Empty : path.TrimStart('$').TrimStart('.');

    private static string Describe(JsonException ex) =>
        ex.Message.Contains("could not be mapped", StringComparison.Ordinal)
            ? "isn't part of the export format."
            : "isn't the right kind of value here.";
}
