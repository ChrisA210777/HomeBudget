using System.Globalization;

namespace Budget.Shared.Income;

/// <summary>
/// The form's live "per pay / per year" preview. It uses the same rules as the domain's
/// PayCalculator (a test keeps the two in step): 52, 13 or 12 periods a year, rounded to pennies
/// for display.
/// </summary>
public static class PayEstimator
{
    public static int PeriodsPerYear(PayFrequencyDto frequency) => frequency switch
    {
        PayFrequencyDto.Weekly => 52,
        PayFrequencyDto.FourWeekly => 13,
        PayFrequencyDto.Monthly => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, null),
    };

    public static int PeriodsPerYear(AmountBasisDto basis) => basis switch
    {
        AmountBasisDto.Annual => 1,
        AmountBasisDto.Monthly => 12,
        AmountBasisDto.FourWeekly => 13,
        AmountBasisDto.Weekly => 52,
        _ => throw new ArgumentOutOfRangeException(nameof(basis), basis, null),
    };

    /// <returns>(per pay, per year) for fixed pay, rounded to pennies.</returns>
    public static (decimal PerPay, decimal PerYear) Fixed(decimal amount, AmountBasisDto basis, PayFrequencyDto frequency)
    {
        var annual = amount * PeriodsPerYear(basis);
        return (Round(annual / PeriodsPerYear(frequency)), Round(annual));
    }

    /// <returns>The yearly equivalent of an amount received every pay period, rounded to pennies.</returns>
    public static decimal PerYear(decimal perPay, PayFrequencyDto frequency) => Round(perPay * PeriodsPerYear(frequency));

    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Formats money the same way everywhere, e.g. "£2,307.69".</summary>
public static class Money
{
    public static string Format(decimal amount, string currencyCode = "GBP") =>
        Symbol(currencyCode) + amount.ToString("N2", CultureInfo.InvariantCulture);

    public static string Symbol(string currencyCode) => currencyCode switch
    {
        "GBP" => "£",
        "EUR" => "€",
        "USD" => "$",
        _ => currencyCode + " ",
    };
}

/// <summary>Words for showing the income enums.</summary>
public static class IncomeLabels
{
    public static string Frequency(PayFrequencyDto frequency) => frequency switch
    {
        PayFrequencyDto.Weekly => "Weekly",
        PayFrequencyDto.FourWeekly => "4-weekly",
        PayFrequencyDto.Monthly => "Monthly",
        _ => frequency.ToString(),
    };

    /// <summary>"week", "4 weeks", "month": used in "per …" phrases.</summary>
    public static string Period(PayFrequencyDto frequency) => frequency switch
    {
        PayFrequencyDto.Weekly => "weekly pay",
        PayFrequencyDto.FourWeekly => "4-weekly pay",
        PayFrequencyDto.Monthly => "monthly pay",
        _ => "pay",
    };

    public static string Basis(AmountBasisDto basis) => basis switch
    {
        AmountBasisDto.Annual => "a year",
        AmountBasisDto.Monthly => "a month",
        AmountBasisDto.FourWeekly => "every 4 weeks",
        AmountBasisDto.Weekly => "a week",
        _ => basis.ToString(),
    };

    public static string Kind(IncomeKindDto kind) => kind switch
    {
        IncomeKindDto.Fixed => "Fixed",
        IncomeKindDto.Variable => "Variable",
        _ => kind.ToString(),
    };
}
