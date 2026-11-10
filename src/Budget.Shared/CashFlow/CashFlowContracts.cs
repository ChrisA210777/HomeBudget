using System.Globalization;

namespace Budget.Shared.CashFlow;

/// <summary>
/// Cash flow (BUD-92, ADR-014): money into and out of each account, with pots counted in their account. Only while
/// the household has "Accounts and transfers" on.
/// </summary>
public static class CashFlowRoutes
{
    /// <summary>GET (Viewers): each account's budget amounts in and out for a month.</summary>
    public static string Month(Guid householdId, int year, int month) =>
        $"/api/households/{householdId}/cash-flow/month?month={year:D4}-{month:D2}";

    /// <summary>GET (Viewers): what goes in and out of each account, day by day, from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static string Days(Guid householdId, DateOnly from, DateOnly to) =>
        string.Create(CultureInfo.InvariantCulture, $"/api/households/{householdId}/cash-flow/days?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
}

public enum CashFlowKindDto
{
    Income,
    Outgoing,
    TransferIn,
    TransferOut,

    /// <summary>A savings outgoing arriving in the account it's saved into (BUD-100).</summary>
    SavingsIn,
}

/// <summary>One amount in or out of an account.</summary>
/// <param name="Id">The income, outgoing or transfer, for linking to it.</param>
/// <param name="Name">e.g. "Salary", "Council tax", or a transfer's name or "From Chris current".</param>
/// <param name="Pot">The pot it goes into or comes from, if it's not the account itself.</param>
/// <param name="Amount">Always positive; <see cref="Kind"/> says which way it goes.</param>
public sealed record CashFlowEntry(CashFlowKindDto Kind, Guid Id, string Name, string? Pot, decimal Amount)
{
    public bool IsIn => Kind is CashFlowKindDto.Income or CashFlowKindDto.TransferIn or CashFlowKindDto.SavingsIn;
}

/// <summary>
/// An account's month, using budget amounts (ADR-011, ADR-013), so it matches the Budget page. Transfers between
/// an account and its own pots are left out, since the money stays in the account.
/// </summary>
/// <param name="AccountId">Null for income and outgoings with no account set.</param>
public sealed record CashFlowMonthAccount(
    Guid? AccountId,
    string Name,
    IReadOnlyList<CashFlowEntry> In,
    IReadOnlyList<CashFlowEntry> Out,
    decimal TotalIn,
    decimal TotalOut)
{
    /// <summary>Positive for a surplus, negative for a shortfall.</summary>
    public decimal Net => TotalIn - TotalOut;
}

public sealed record CashFlowMonthResponse(int Year, int Month, string CurrencyCode, IReadOnlyList<CashFlowMonthAccount> Accounts);

/// <summary>One day for an account: what comes in and goes out, and the running total from the start of the period.</summary>
/// <param name="IsShort">More has gone out than come in so far: a timing gap.</param>
public sealed record CashFlowDay(DateOnly Date, IReadOnlyList<CashFlowEntry> Entries, decimal In, decimal Out, decimal RunningTotal, bool IsShort);

/// <summary>An account's payments day by day (ADR-013), only on days something happens.</summary>
public sealed record CashFlowDaysAccount(Guid? AccountId, string Name, IReadOnlyList<CashFlowDay> Days, decimal TotalIn, decimal TotalOut)
{
    public decimal Net => TotalIn - TotalOut;
}

public sealed record CashFlowDaysResponse(DateOnly From, DateOnly To, string CurrencyCode, IReadOnlyList<CashFlowDaysAccount> Accounts);
