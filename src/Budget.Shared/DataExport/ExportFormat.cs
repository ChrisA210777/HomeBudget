using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Budget.Shared.DataExport;

/// <summary>Constants and settings for the export file (BUD-64).</summary>
public static class ExportFormat
{
    public const string FormatName = "home-budget-export";

    /// <summary>The version this app writes. It reads this and every earlier version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Files bigger than this are refused.</summary>
    public const long MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>How the file is written and read: camelCase, indented, nulls left out.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Readable £ signs and accents; the file is never put into HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // A misspelt property is a mistake in the file, not something to ignore.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
}

/// <summary>The names enum values are written with in schema version 1.</summary>
public static class ExportValues
{
    public static IReadOnlyList<string> Roles { get; } = ["Viewer", "User", "Admin"];

    public static IReadOnlyList<string> Units { get; } = ["Week", "Month", "Year"];

    public static IReadOnlyList<string> IncomeStatusReasons { get; } = ["Holiday", "SickLeave", "UnpaidLeave", "ParentalLeave", "Ended", "Other"];

    public static IReadOnlyList<string> OutgoingStatusReasons { get; } = ["Paused", "Ended"];

    public static IReadOnlyList<string> Kinds { get; } = ["Bill", "Savings", "Allowance"];

    public static IReadOnlyList<string> Priorities { get; } = ["Essential", "Preferred", "Optional"];

    public static IReadOnlyList<string> PaymentMethods { get; } = ["DirectDebit", "StandingOrder", "Cash", "CreditCard", "DebitCard", "Cheque", "BankTransfer", "Other"];

    public static IReadOnlyList<string> AlertPeriods { get; } =
        ["None", "OneDay", "TwoDays", "ThreeDays", "OneWeek", "TwoWeeks", "OneMonth", "TwoMonths", "ThreeMonths", "SixMonths"];

    public static IReadOnlyList<string> EntryTypes { get; } = ["AmountChange", "StatusPeriod"];

    public static IReadOnlyList<string> CorrectionActions { get; } = ["Edited", "Deleted"];
}

/// <summary>Routes for exporting and importing (BUD-61).</summary>
public static class DataRoutes
{
    /// <summary>GET (Admins): download the household's data.</summary>
    public static string Export(Guid householdId, bool includeHistory, bool includeAudit) =>
        $"/api/households/{householdId}/export?history={(includeHistory ? "true" : "false")}&audit={(includeAudit ? "true" : "false")}";

    /// <summary>POST (Admins) the file's bytes: checks it and says what's in it, without saving anything (BUD-67).</summary>
    public static string Preview(Guid householdId) => $"/api/households/{householdId}/import/preview";
}

/// <summary>A problem with an import file and where it is, e.g. <c>outgoings[3].amountChanges[0].amount</c>.</summary>
public sealed record ImportProblem(string Path, string Message);

/// <summary>What an import file contains, and whether it can be imported (BUD-67).</summary>
public sealed record ImportPreviewResponse
{
    /// <summary>True when there are no errors, so the file can be imported.</summary>
    public bool CanImport { get; init; }

    public IReadOnlyList<ImportProblem> Errors { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>The format version the file was written in, if it could be read.</summary>
    public int? FileVersion { get; init; }

    public string? HouseholdName { get; init; }

    public string? CurrencyCode { get; init; }

    public DateTimeOffset? ExportedAt { get; init; }

    public string? AppVersion { get; init; }

    public bool IncludesHistory { get; init; }

    public bool IncludesAudit { get; init; }

    public ImportCounts Counts { get; init; } = new();

    /// <summary>The members listed in the file, to invite again after importing.</summary>
    public IReadOnlyList<ImportMember> Members { get; init; } = [];
}

public sealed record ImportCounts
{
    public int People { get; init; }

    public int Categories { get; init; }

    public int Incomes { get; init; }

    public int Outgoings { get; init; }

    /// <summary>Amount changes, status periods and corrections, across incomes and outgoings.</summary>
    public int HistoryEntries { get; init; }
}

public sealed record ImportMember(string DisplayName, string? Email, string Role, bool IsOwner);
