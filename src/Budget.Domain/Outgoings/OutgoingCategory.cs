using Budget.Domain.Common;
using Budget.Domain.Households;

namespace Budget.Domain.Outgoings;

/// <summary>
/// A household's own label for grouping outgoings, e.g. "Housing" (BUD-47). Every household starts
/// with <see cref="DefaultNames"/>. Hidden categories aren't offered for new outgoings but stay on
/// existing ones.
/// </summary>
public sealed class OutgoingCategory : AuditableEntity, IHouseholdOwned
{
    public const int NameMaxLength = 50;

    /// <summary>The categories every household starts with (ADR-011), in display order.</summary>
    public static IReadOnlyList<string> DefaultNames { get; } =
    [
        "Housing",
        "Utilities",
        "Phone & internet",
        "Insurance",
        "Home maintenance",
        "Car & travel",
        "Pets",
        "Health & personal care",
        "Food & household",
        "Children",
        "Subscriptions & TV",
        "Leisure & memberships",
        "Gifts & giving",
        "Banking & fees",
        "Savings & set-asides",
        "Discretionary spending",
    ];

    private OutgoingCategory()
    {
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Where it appears in lists; new categories go at the end.</summary>
    public int SortOrder { get; private set; }

    public bool IsHidden { get; private set; }

    public static OutgoingCategory Create(Guid householdId, string name, int sortOrder)
    {
        if (householdId == Guid.Empty)
        {
            throw new ArgumentException("A category must belong to a household.", nameof(householdId));
        }

        return new OutgoingCategory
        {
            Id = Guid.CreateVersion7(),
            HouseholdId = householdId,
            Name = NormaliseName(name),
            SortOrder = sortOrder,
        };
    }

    /// <summary>The default categories for a new household.</summary>
    public static IReadOnlyList<OutgoingCategory> CreateDefaults(Guid householdId) =>
        DefaultNames.Select((name, index) => Create(householdId, name, (index + 1) * 10)).ToList();

    public void Rename(string name) => Name = NormaliseName(name);

    public void Hide() => IsHidden = true;

    public void Show() => IsHidden = false;

    public static string NormaliseName(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A category name is required.", nameof(name));
        }

        if (trimmed.Length > NameMaxLength)
        {
            throw new ArgumentException($"A category name can be at most {NameMaxLength} characters.", nameof(name));
        }

        return trimmed;
    }
}
