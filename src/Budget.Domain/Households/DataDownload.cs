namespace Budget.Domain.Households;

/// <summary>The kind of file a household's data was downloaded as.</summary>
public enum DataDownloadFormat
{
    /// <summary>The JSON file that can be imported again (BUD-65).</summary>
    Json,

    /// <summary>The zip of CSV files for Excel (BUD-66).</summary>
    Csv,
}

/// <summary>
/// One download of a household's data (BUD-141): who, when, and what. For security rather than visibility: only the
/// household's Admins see these, they're kept for twelve months, can't be edited, and aren't exported. No names are
/// stored; the person is looked up when the log is shown, and is "A former member" once their account is deleted.
/// </summary>
public sealed class DataDownload : IHouseholdOwned
{
    /// <summary>How long entries are kept.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(365);

    private DataDownload()
    {
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public DateTimeOffset At { get; private set; }

    /// <summary>Who downloaded it; <c>null</c> once their account is deleted.</summary>
    public Guid? UserId { get; private set; }

    public DataDownloadFormat Format { get; private set; }

    public bool IncludesHistory { get; private set; }

    public bool IncludesAudit { get; private set; }

    public static DataDownload Record(Guid householdId, Guid userId, DateTimeOffset at, DataDownloadFormat format, bool includesHistory, bool includesAudit) =>
        new()
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            UserId = userId,
            At = at,
            Format = format,
            IncludesHistory = includesHistory,
            IncludesAudit = includesAudit,
        };
}
