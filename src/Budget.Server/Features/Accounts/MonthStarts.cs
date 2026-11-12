using Budget.Domain.Accounts;
using Budget.Domain.Income;
using Budget.Domain.Transfers;
using Budget.Shared.Accounts;

namespace Budget.Server.Features.Accounts;

/// <summary>
/// When an account's month starts (BUD-136): on the 1st, or when a chosen income or transfer arrives in it. The
/// choices are the account's regular money in: income paid into it or its pots, and transfers into it or its pots from
/// another account (a transfer from its own pot leaves the money in the account).
/// </summary>
public static class MonthStarts
{
    /// <summary>The income and transfers that can start <paramref name="account"/>'s month, by name. None for a pot.</summary>
    public static IReadOnlyList<MonthStartOption> Options(
        Account account, AccountDirectory directory, IEnumerable<IncomeStream> incomes, IEnumerable<Transfer> transfers)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(directory);
        if (account.IsPot)
        {
            return [];
        }

        bool InAccount(Guid? id) => directory.Find(id) is { } a && (a.Id == account.Id || a.ParentAccountId == account.Id);

        return incomes.Where(i => InAccount(i.AccountId)).Select(i => new MonthStartOption(i.Id, i.Name))
            .Concat(transfers.Where(t => InAccount(t.ToAccountId) && !InAccount(t.FromAccountId))
                .Select(t => new MonthStartOption(t.Id, Name(t, directory))))
            .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>A transfer's name, or "From Chris current" when it has none.</summary>
    public static string Name(Transfer transfer, AccountDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(directory);
        return transfer.Name ?? $"From {directory.DisplayName(transfer.FromAccountId)}";
    }
}
