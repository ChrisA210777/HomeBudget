namespace Budget.Domain.Income;

/// <summary>
/// Works out income from a stream's timeline (see ADR-009):
/// <list type="bullet">
/// <item>On a date: the latest amount change on or before it, replaced by the amount of any status period covering it.</item>
/// <item>For a month: pro-rata by calendar days, i.e. the average of the daily amounts in force.</item>
/// </list>
/// Results are unrounded; round once for display with <see cref="RoundForDisplay"/>.
/// </summary>
public static class IncomeTimeline
{
    public static decimal AmountOn(
        IEnumerable<IncomeAmountChange> amountChanges,
        IEnumerable<IncomeStatusPeriod> statusPeriods,
        DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(amountChanges);
        ArgumentNullException.ThrowIfNull(statusPeriods);

        var period = statusPeriods.FirstOrDefault(p => p.Covers(date));
        if (period is not null)
        {
            return period.ReducedMonthlyAmount ?? 0m;
        }

        return amountChanges
            .Where(c => c.EffectiveFrom <= date)
            .OrderByDescending(c => c.EffectiveFrom)
            .Select(c => c.MonthlyNetAmount)
            .FirstOrDefault();
    }

    /// <summary>The income for a calendar month, pro-rata by day.</summary>
    public static decimal AmountForMonth(
        IReadOnlyCollection<IncomeAmountChange> amountChanges,
        IReadOnlyCollection<IncomeStatusPeriod> statusPeriods,
        int year,
        int month)
    {
        var days = DateTime.DaysInMonth(year, month);
        var total = 0m;
        for (var day = 1; day <= days; day++)
        {
            total += AmountOn(amountChanges, statusPeriods, new DateOnly(year, month, day));
        }

        // Divide once at the end so a month with no changes comes out exactly at the monthly amount.
        return total / days;
    }

    /// <summary>Rounds to pennies for display: 2 decimal places, halves rounded away from zero.</summary>
    public static decimal RoundForDisplay(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Checks an amount is positive, sensible and in pounds and pence.</summary>
    public static decimal ValidAmount(decimal amount, string paramName)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, amount, "Amounts must be greater than zero.");
        }

        if (amount > IncomeStream.MaxAmount)
        {
            throw new ArgumentOutOfRangeException(paramName, amount, "That amount is too large.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentOutOfRangeException(paramName, amount, "Amounts can have at most 2 decimal places.");
        }

        return amount;
    }

    /// <summary>Trims a comment or reason and checks it's present and not too long.</summary>
    public static string RequiredText(string? text, string paramName, int maxLength = IncomeStream.CommentMaxLength)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("This is required.", paramName);
        }

        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"This can be at most {maxLength} characters.", paramName);
        }

        return trimmed;
    }
}
