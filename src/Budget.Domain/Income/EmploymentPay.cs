namespace Budget.Domain.Income;

/// <summary>How much someone is paid for a job: either a fixed amount or a variable range.</summary>
public abstract record EmploymentPay
{
    /// <summary>The largest amount accepted, to catch typing mistakes (e.g. extra zeros).</summary>
    public const decimal MaxAmount = 100_000_000m;

    private protected EmploymentPay()
    {
    }

    public abstract IncomeKind Kind { get; }

    public abstract IncomeEstimate Estimate(PayFrequency frequency);

    private protected static decimal ValidAmount(decimal amount, string name)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(name, amount, "Amounts must be greater than zero.");
        }

        if (amount > MaxAmount)
        {
            throw new ArgumentOutOfRangeException(name, amount, $"Amounts can be at most {MaxAmount:N0}.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentOutOfRangeException(name, amount, "Amounts can have at most 2 decimal places.");
        }

        return amount;
    }
}

/// <summary>The same gross pay every period, entered for a chosen period (e.g. an annual salary).</summary>
public sealed record FixedPay : EmploymentPay
{
    public FixedPay(decimal grossAmount, AmountBasis basis)
    {
        if (!Enum.IsDefined(basis))
        {
            throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unknown amount basis.");
        }

        GrossAmount = ValidAmount(grossAmount, nameof(grossAmount));
        Basis = basis;
    }

    /// <summary>The amount exactly as entered.</summary>
    public decimal GrossAmount { get; }

    /// <summary>The period <see cref="GrossAmount"/> covers.</summary>
    public AmountBasis Basis { get; }

    public override IncomeKind Kind => IncomeKind.Fixed;

    public override IncomeEstimate Estimate(PayFrequency frequency)
    {
        var annual = PayCalculator.ToAnnual(GrossAmount, Basis);
        var perPay = PayCalculator.PerPay(annual, frequency);
        return new IncomeEstimate(perPay, perPay, perPay, annual, annual, annual);
    }
}

/// <summary>Gross pay that changes between pay periods: the lowest, highest and average per pay period.</summary>
public sealed record VariablePay : EmploymentPay
{
    public VariablePay(decimal lowestGross, decimal averageGross, decimal highestGross)
    {
        LowestGross = ValidAmount(lowestGross, nameof(lowestGross));
        AverageGross = ValidAmount(averageGross, nameof(averageGross));
        HighestGross = ValidAmount(highestGross, nameof(highestGross));

        if (!(lowestGross <= averageGross && averageGross <= highestGross))
        {
            throw new ArgumentException("Lowest must not be more than average, and average must not be more than highest.");
        }
    }

    public decimal LowestGross { get; }

    public decimal AverageGross { get; }

    public decimal HighestGross { get; }

    public override IncomeKind Kind => IncomeKind.Variable;

    public override IncomeEstimate Estimate(PayFrequency frequency) => new(
        LowestGross,
        AverageGross,
        HighestGross,
        PayCalculator.ToAnnual(LowestGross, frequency),
        PayCalculator.ToAnnual(AverageGross, frequency),
        PayCalculator.ToAnnual(HighestGross, frequency));
}
