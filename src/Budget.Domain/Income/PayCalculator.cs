namespace Budget.Domain.Income;

/// <summary>
/// Converts pay between periods using UK payroll conventions: 52 weeks, 13 four-week periods
/// or 12 months in a year (see ADR-007). Calculations keep full precision; round only for display.
/// </summary>
public static class PayCalculator
{
    public static int PeriodsPerYear(PayFrequency frequency) => frequency switch
    {
        PayFrequency.Weekly => 52,
        PayFrequency.FourWeekly => 13,
        PayFrequency.Monthly => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Unknown pay frequency."),
    };

    public static int PeriodsPerYear(AmountBasis basis) => basis switch
    {
        AmountBasis.Annual => 1,
        AmountBasis.Monthly => 12,
        AmountBasis.FourWeekly => 13,
        AmountBasis.Weekly => 52,
        _ => throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unknown amount basis."),
    };

    /// <summary>The yearly equivalent of <paramref name="amount"/> entered for <paramref name="basis"/>.</summary>
    public static decimal ToAnnual(decimal amount, AmountBasis basis) => amount * PeriodsPerYear(basis);

    /// <summary>The yearly equivalent of <paramref name="amount"/> received every pay period.</summary>
    public static decimal ToAnnual(decimal amount, PayFrequency frequency) => amount * PeriodsPerYear(frequency);

    /// <summary>What a yearly amount works out to for each pay period.</summary>
    public static decimal PerPay(decimal annual, PayFrequency frequency) => annual / PeriodsPerYear(frequency);

    /// <summary>A yearly amount spread over 12 months.</summary>
    public static decimal PerMonth(decimal annual) => annual / 12;

    /// <summary>Rounds to pennies for display: 2 decimal places, halves rounded away from zero.</summary>
    public static decimal RoundForDisplay(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Gross income in each pay period and per year. Fixed pay has the same low, average and high.
/// Values are unrounded; use <see cref="PayCalculator.RoundForDisplay"/> to show them.
/// </summary>
public sealed record IncomeEstimate(
    decimal LowPerPay,
    decimal AveragePerPay,
    decimal HighPerPay,
    decimal LowAnnual,
    decimal AverageAnnual,
    decimal HighAnnual)
{
    public static readonly IncomeEstimate None = new(0, 0, 0, 0, 0, 0);
}
