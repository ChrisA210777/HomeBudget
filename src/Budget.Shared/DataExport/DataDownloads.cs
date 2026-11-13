namespace Budget.Shared.DataExport;

/// <summary>Where the data download log is read (BUD-141).</summary>
public static class DataDownloadRoutes
{
    /// <summary>GET (Admins): who downloaded the household's data in the last twelve months, newest first.</summary>
    public static string List(Guid householdId) => $"/api/households/{householdId}/export/downloads";

    /// <summary>How long downloads are kept and shown, in months.</summary>
    public const int KeptForMonths = 12;
}

/// <summary>One download of the household's data (BUD-141).</summary>
/// <param name="Who">The person's name now, or "A former member" if their account has been deleted.</param>
/// <param name="IsYou">The person looking at the log made this download.</param>
/// <param name="Format">"Json" or "Csv".</param>
public sealed record DataDownloadSummary(DateTimeOffset At, string Who, bool IsYou, string Format, bool IncludesHistory, bool IncludesAudit);
