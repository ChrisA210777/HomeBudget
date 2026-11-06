namespace Budget.Domain.Households;

/// <summary>A change to a household's members that its rules don't allow. The message is shown to the user.</summary>
public sealed class HouseholdMembershipException : Exception
{
    public HouseholdMembershipException()
    {
    }

    public HouseholdMembershipException(string message)
        : base(message)
    {
    }

    public HouseholdMembershipException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
