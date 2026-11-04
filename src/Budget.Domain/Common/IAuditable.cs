namespace Budget.Domain.Common;

/// <summary>
/// An entity whose creation and last modification are recorded. The values are set
/// automatically when changes are saved; application code never sets them directly.
/// </summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }

    /// <summary>The user who created the entity, or <c>null</c> if it was created by the system.</summary>
    Guid? CreatedBy { get; }

    DateTimeOffset ModifiedAt { get; }

    /// <summary>The user who last modified the entity, or <c>null</c> if it was modified by the system.</summary>
    Guid? ModifiedBy { get; }
}
