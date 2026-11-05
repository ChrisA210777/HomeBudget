namespace Budget.Domain.Common;

/// <summary>Checks shared by the money entities.</summary>
public static class Values
{
    public const decimal MaxAmount = 10_000_000m;

    /// <summary>Checks an amount is positive, sensible and in pounds and pence.</summary>
    public static decimal Amount(decimal amount, string paramName)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, amount, "Amounts must be greater than zero.");
        }

        if (amount > MaxAmount)
        {
            throw new ArgumentOutOfRangeException(paramName, amount, "That amount is too large.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentOutOfRangeException(paramName, amount, "Amounts can have at most 2 decimal places.");
        }

        return amount;
    }

    /// <summary>Trims text and checks it's present and not too long.</summary>
    public static string Required(string? text, string paramName, int maxLength)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("This is required.", paramName);
        }

        return MaxLength(trimmed, paramName, maxLength);
    }

    /// <summary>Trims text; blank becomes <c>null</c>.</summary>
    public static string? Optional(string? text, string paramName, int maxLength)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : MaxLength(trimmed, paramName, maxLength);
    }

    /// <summary>Rounds to pennies for display: 2 decimal places, halves rounded away from zero.</summary>
    public static decimal RoundForDisplay(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string MaxLength(string text, string paramName, int maxLength) =>
        text.Length <= maxLength ? text : throw new ArgumentException($"This can be at most {maxLength} characters.", paramName);
}
