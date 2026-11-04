namespace Budget.Domain.Households;

/// <summary>
/// Data that belongs to exactly one household, such as an income stream. Queries for these
/// entities only ever return the current household's rows (see ADR-003).
/// </summary>
public interface IHouseholdOwned
{
    Guid HouseholdId { get; }
}
