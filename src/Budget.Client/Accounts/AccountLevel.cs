using Budget.Shared.Accounts;

namespace Budget.Client.Accounts;

/// <summary>
/// Budgeting is done by account; pots only keep money apart within one (BUD-95). So for filtering, grouping and
/// totals, an outgoing paid from a pot counts as its account.
/// </summary>
public static class AccountLevel
{
    /// <summary>
    /// The account (not the pot) behind an account id, with the pot's name if it was a pot. Falls back to the
    /// "Account › Pot" name if the id isn't in the list; nothing if no account is recorded.
    /// </summary>
    public static (Guid? Id, string? Name, string? Pot) Of(Guid? accountId, string? paidFrom, IReadOnlyList<AccountSummary> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        if (accountId is { } id && accounts.FirstOrDefault(a => a.Id == id) is { } account)
        {
            var parent = account.ParentAccountId is { } parentId ? accounts.FirstOrDefault(a => a.Id == parentId) : null;
            return parent is null ? (account.Id, account.Name, null) : (parent.Id, parent.Name, account.Name);
        }

        if (string.IsNullOrWhiteSpace(paidFrom))
        {
            return (null, null, null);
        }

        var parts = paidFrom.Split('›', 2);
        return (accountId, parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : null);
    }
}
