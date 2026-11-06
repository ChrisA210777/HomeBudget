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
}
