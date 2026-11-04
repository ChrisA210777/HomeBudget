using Budget.Domain.Common;
using Budget.Domain.Households;

namespace Budget.Domain.Income;

/// <summary>
/// Money coming into a household. Each type of income (only <see cref="EmploymentIncome"/> for now)
/// derives from this and is stored in one table with a <see cref="Type"/> column (see ADR-003).
/// </summary>
public abstract class IncomeStream : AuditableEntity, IHouseholdOwned
{
    public const int NameMaxLength = 100;

    private protected IncomeStream()
    {
    }

    private protected IncomeStream(Guid householdId, IncomeStreamType type)
    {
        if (householdId == Guid.Empty)
        {
            throw new ArgumentException("An income stream must belong to a household.", nameof(householdId));
        }

        Id = Guid.CreateVersion7();
        HouseholdId = householdId;
        Type = type;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public IncomeStreamType Type { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>The household member this income belongs to, if any.</summary>
    public Guid? MemberUserId { get; private set; }

    /// <summary>Inactive streams (e.g. a job that has ended) are kept but left out of totals.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gross income per pay period and per year.</summary>
    public abstract IncomeEstimate Estimate();

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    /// <summary>Clears the link to a member, e.g. when they leave the household.</summary>
    public void ClearMember() => MemberUserId = null;

    private protected void SetCommonDetails(string name, Guid? memberUserId)
    {
        Name = NormaliseName(name);
        MemberUserId = memberUserId == Guid.Empty ? null : memberUserId;
    }

    private static string NormaliseName(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A name is required.", nameof(name));
        }

        if (trimmed.Length > NameMaxLength)
        {
            throw new ArgumentException($"A name can be at most {NameMaxLength} characters.", nameof(name));
        }

        return trimmed;
    }
}
