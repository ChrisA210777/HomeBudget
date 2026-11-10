using Budget.Domain.Accounts;
using Budget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Accounts;

/// <summary>
/// A household's accounts and pots (BUD-89, ADR-014), loaded once per request: shows them by name, e.g.
/// "Joint Monzo › Holiday", and turns a typed name into an account, adding the account or pot if it's new.
/// </summary>
public sealed class AccountDirectory
{
    private readonly List<Account> _accounts;
    private readonly Action<Account> _add;
    private readonly Guid _householdId;

    private AccountDirectory(Guid householdId, List<Account> accounts, Action<Account> add)
    {
        _householdId = householdId;
        _accounts = accounts;
        _add = add;
    }

    public IReadOnlyList<Account> All => _accounts;

    /// <summary>The current household's accounts, tracked so new ones are saved with the request's other changes.</summary>
    public static async Task<AccountDirectory> LoadAsync(BudgetDbContext db, Guid householdId)
    {
        ArgumentNullException.ThrowIfNull(db);
        return new AccountDirectory(householdId, await db.Accounts.ToListAsync(), account => db.Accounts.Add(account));
    }

    /// <summary>For building a new household's accounts outside the database, e.g. an import (BUD-89).</summary>
    public static AccountDirectory InMemory(Guid householdId, List<Account> accounts) =>
        new(householdId, accounts, accounts.Add);

    public Account? Find(Guid? id) => id is { } value ? _accounts.Find(a => a.Id == value) : null;

    /// <summary>"Account" or "Account › Pot"; <c>null</c> when there's no account.</summary>
    public string? DisplayName(Guid? id) =>
        Find(id) is { } account ? account.DisplayName(Find(account.ParentAccountId)) : null;

    /// <summary>Every account and pot that can be chosen, by name.</summary>
    public IReadOnlyList<string> ActiveNames() =>
        _accounts
            .Where(a => !a.IsArchived && !(Find(a.ParentAccountId)?.IsArchived ?? false))
            .Select(a => a.DisplayName(Find(a.ParentAccountId)))
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// The account or pot a typed name means, ignoring case and spaces: "Account", "Account › Pot" or
    /// "Account (Pot Pot)". Anything new is added, and an archived match is restored. Blank means no account.
    /// </summary>
    public Account? Resolve(string? text, bool restoreArchived = true)
    {
        if (AccountNames.Parse(text) is not var (accountName, potName))
        {
            return null;
        }

        var account = Match(accountName, parentId: null)
            ?? Added(Account.Create(_householdId, Trimmed(accountName)));
        if (restoreArchived)
        {
            account.Restore();
        }

        if (potName is null)
        {
            return account;
        }

        var pot = Match(potName, account.Id) ?? Added(Account.CreatePot(account, Trimmed(potName)));
        if (restoreArchived)
        {
            pot.Restore();
        }

        return pot;
    }

    /// <summary>Whether another account (or pot in the same account) already has this name.</summary>
    public bool NameTaken(string name, Guid? parentId, Guid? exceptId = null) =>
        _accounts.Any(a => a.ParentAccountId == parentId && a.Id != exceptId && Same(a.Name, name));

    private Account? Match(string name, Guid? parentId) =>
        _accounts.Where(a => a.ParentAccountId == parentId && Same(a.Name, name)).OrderBy(a => a.IsArchived).FirstOrDefault();

    private Account Added(Account account)
    {
        _add(account);
        if (!_accounts.Contains(account))
        {
            _accounts.Add(account);
        }

        return account;
    }

    private static string Trimmed(string name) => name.Length > Account.NameMaxLength ? name[..Account.NameMaxLength].Trim() : name;

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.CurrentCultureIgnoreCase);
}
