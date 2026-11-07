namespace Budget.Domain.Common;

/// <summary>
/// Base class for entities that record who created and last modified them, and when.
/// The setters are private: the persistence layer fills these in when changes are saved.
/// </summary>
public abstract class AuditableEntity : IAuditable
{
    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset ModifiedAt { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public const int OriginallyRecordedByMaxLength = 200;

    /// <summary>
    /// For data brought in from an export file (BUD-63): who first recorded it, by name, in the household it came
    /// from. <see cref="CreatedBy"/> is then whoever imported it.
    /// </summary>
    public string? OriginallyRecordedBy { get; private set; }

    /// <summary>For imported data: when it was first recorded in the household it came from.</summary>
    public DateTimeOffset? OriginallyRecordedAt { get; private set; }

    /// <summary>Who recorded it, by name if it was imported; otherwise <c>null</c> (look up <see cref="CreatedBy"/>).</summary>
    public string? RecordedByName => OriginallyRecordedBy;

    /// <summary>When it was first recorded: the original time for imported data.</summary>
    public DateTimeOffset RecordedAt => OriginallyRecordedAt ?? CreatedAt;

    /// <summary>Keeps who first recorded this, and when, from an export file (BUD-63).</summary>
    public void RestoreOriginalRecord(string? recordedBy, DateTimeOffset? recordedAt)
    {
        var name = recordedBy?.Trim();
        OriginallyRecordedBy = string.IsNullOrEmpty(name) ? null : name.Length > OriginallyRecordedByMaxLength ? name[..OriginallyRecordedByMaxLength] : name;
        OriginallyRecordedAt = recordedAt;
    }
}
