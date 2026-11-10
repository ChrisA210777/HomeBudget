using System.Text.RegularExpressions;

namespace Budget.Domain.Accounts;

/// <summary>
/// Reads an account name as typed, or as stored before accounts existed (ADR-014): "Account › Pot", or the
/// older "Account (Pot Pot)" style, e.g. "Joint Monzo Account (Holiday Pot)", are a pot in an account; anything
/// else is an account. Spaces at either end are ignored.
/// </summary>
public static partial class AccountNames
{
    public static (string Account, string? Pot)? Parse(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var separator = trimmed.IndexOf(Account.PotSeparator.Trim(), StringComparison.Ordinal);
        if (separator > 0 && separator < trimmed.Length - 1)
        {
            return (trimmed[..separator].Trim(), trimmed[(separator + 1)..].Trim());
        }

        var pot = PotInBrackets().Match(trimmed);
        return pot.Success ? (pot.Groups["account"].Value.Trim(), pot.Groups["pot"].Value.Trim()) : (trimmed, null);
    }

    /// <summary>"Joint (Holiday Pot)": a name, then a pot's name in brackets ending in "Pot", ignoring case.</summary>
    [GeneratedRegex(@"^(?<account>.*\S)\s*\((?<pot>.*\S)\s+pot\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PotInBrackets();
}
