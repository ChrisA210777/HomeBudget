namespace Budget.Domain.Income;

/// <summary>The kind of income stream. Only Employment for now; more will follow.</summary>
public enum IncomeStreamType
{
    Employment,
}

/// <summary>How often someone is paid.</summary>
public enum PayFrequency
{
    /// <summary>52 times a year.</summary>
    Weekly,

    /// <summary>13 times a year.</summary>
    FourWeekly,

    /// <summary>12 times a year.</summary>
    Monthly,
}

/// <summary>Whether pay is the same every pay period or varies.</summary>
public enum IncomeKind
{
    Fixed,
    Variable,
}

/// <summary>The period a fixed gross amount was entered for, e.g. an annual salary.</summary>
public enum AmountBasis
{
    Annual,
    Monthly,
    FourWeekly,
    Weekly,
}
