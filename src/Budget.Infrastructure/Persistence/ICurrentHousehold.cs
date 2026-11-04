namespace Budget.Infrastructure.Persistence;

/// <summary>
/// The household the current request is working in. It is only set after the request has been
/// authorised as a member of that household, and every <c>IHouseholdOwned</c> query is
/// filtered to it.
/// </summary>
public interface ICurrentHousehold
{
    /// <summary>The household id, or <c>null</c> when the request isn't scoped to a household.</summary>
    Guid? HouseholdId { get; }
}

/// <summary>The request-scoped <see cref="ICurrentHousehold"/>; set once by the authorisation check.</summary>
public sealed class CurrentHousehold : ICurrentHousehold
{
    public Guid? HouseholdId { get; private set; }

    public void Set(Guid householdId)
    {
        if (HouseholdId is { } existing && existing != householdId)
        {
            throw new InvalidOperationException("The current household has already been set for this request.");
        }

        HouseholdId = householdId;
    }
}
