namespace Budget.Domain.Income;

/// <summary>
/// Income from a job. Pay is either fixed (an amount entered for a chosen period) or variable
/// (lowest, average and highest per pay period). Amounts are stored exactly as entered (ADR-007).
/// </summary>
public sealed class EmploymentIncome : IncomeStream
{
    public const int EmployerNameMaxLength = 100;

    private EmploymentIncome()
    {
    }

    private EmploymentIncome(Guid householdId)
        : base(householdId, IncomeStreamType.Employment)
    {
    }

    public string? EmployerName { get; private set; }

    public PayFrequency PayFrequency { get; private set; }

    public IncomeKind Kind { get; private set; }

    // Fixed pay: the amount and the period it was entered for.
    public decimal? GrossAmount { get; private set; }

    public AmountBasis? AmountBasis { get; private set; }

    // Variable pay: per pay period.
    public decimal? LowestGross { get; private set; }

    public decimal? AverageGross { get; private set; }

    public decimal? HighestGross { get; private set; }

    /// <summary>The pay details as a value, rebuilt from the stored columns.</summary>
    public EmploymentPay Pay => Kind switch
    {
        IncomeKind.Fixed => new FixedPay(GrossAmount!.Value, AmountBasis!.Value),
        IncomeKind.Variable => new VariablePay(LowestGross!.Value, AverageGross!.Value, HighestGross!.Value),
        _ => throw new InvalidOperationException($"Unknown income kind {Kind}."),
    };

    public static EmploymentIncome Create(
        Guid householdId,
        string name,
        string? employerName,
        Guid? memberUserId,
        PayFrequency payFrequency,
        EmploymentPay pay)
    {
        var income = new EmploymentIncome(householdId);
        income.Update(name, employerName, memberUserId, payFrequency, pay);
        return income;
    }

    /// <summary>
    /// Replaces the details. Switching between fixed and variable pay clears the other kind's amounts.
    /// </summary>
    public void Update(string name, string? employerName, Guid? memberUserId, PayFrequency payFrequency, EmploymentPay pay)
    {
        ArgumentNullException.ThrowIfNull(pay);
        if (!Enum.IsDefined(payFrequency))
        {
            throw new ArgumentOutOfRangeException(nameof(payFrequency), payFrequency, "Unknown pay frequency.");
        }

        var employer = string.IsNullOrWhiteSpace(employerName) ? null : employerName.Trim();
        if (employer?.Length > EmployerNameMaxLength)
        {
            throw new ArgumentException($"An employer name can be at most {EmployerNameMaxLength} characters.", nameof(employerName));
        }

        SetCommonDetails(name, memberUserId);
        EmployerName = employer;
        PayFrequency = payFrequency;
        SetPay(pay);
    }

    public override IncomeEstimate Estimate() => Pay.Estimate(PayFrequency);

    private void SetPay(EmploymentPay pay)
    {
        Kind = pay.Kind;
        GrossAmount = null;
        AmountBasis = null;
        LowestGross = null;
        AverageGross = null;
        HighestGross = null;

        switch (pay)
        {
            case FixedPay fixedPay:
                GrossAmount = fixedPay.GrossAmount;
                AmountBasis = fixedPay.Basis;
                break;
            case VariablePay variablePay:
                LowestGross = variablePay.LowestGross;
                AverageGross = variablePay.AverageGross;
                HighestGross = variablePay.HighestGross;
                break;
            default:
                throw new ArgumentException($"Unsupported pay type {pay.GetType().Name}.", nameof(pay));
        }
    }
}
