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
}
