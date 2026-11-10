using Budget.Domain.Accounts;
using Budget.Domain.Common;
using Budget.Domain.Outgoings;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Accounts;
using Budget.Server.Features.Households;
using Budget.Shared.CashFlow;
using Budget.Shared.Households;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.CashFlow;

/// <summary>
/// Cash flow (BUD-92, ADR-014): for each account, with its pots, money in (income paid into it, transfers in)
/// against money out (outgoings paid from it, transfers out). Monthly uses budget amounts; day by day uses the
/// payments actually due. Anyone in the household can see it while "Accounts and transfers" is on.
/// </summary>
public static class CashFlowEndpoints
{
    private const int MaxDays = 62;

    public static IEndpointRouteBuilder MapCashFlowEndpoints(this IEndpointRouteBuilder app)
    {
        var cashFlow = app.MapGroup(HouseholdRoutes.Base + "/cash-flow");
        cashFlow.MapGet("/month", MonthAsync).RequireAuthorization(HouseholdPolicies.Viewer).AddEndpointFilter(RequireTransfersEnabledAsync);
        cashFlow.MapGet("/days", DaysAsync).RequireAuthorization(HouseholdPolicies.Viewer).AddEndpointFilter(RequireTransfersEnabledAsync);
        return app;
    }

    private static async ValueTask<object?> RequireTransfersEnabledAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var householdId = Guid.TryParse(context.HttpContext.Request.RouteValues["householdId"]?.ToString(), out var parsed) ? parsed : Guid.Empty;
        var db = context.HttpContext.RequestServices.GetRequiredService<BudgetDbContext>();
        if (!await db.Households.AnyAsync(h => h.Id == householdId && h.TransfersEnabled))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Cash flow is turned off",
                detail: "An Admin can turn on \"Accounts and transfers\" on the Accounts page.");
        }

        return await next(context);
    }

    private static async Task<IResult> MonthAsync(Guid householdId, string? month, BudgetDbContext db, TimeProvider time)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var (year, monthNumber) = (today.Year, today.Month);
        if (month is not null)
        {
            if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var parsed))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["month"] = ["Use the form yyyy-MM."] });
            }

            (year, monthNumber) = (parsed.Year, parsed.Month);
        }

        var data = await LoadAsync(db, householdId);
        var entries = new List<(Guid? Account, string? Pot, CashFlowEntry Entry, decimal Exact)>();

        foreach (var income in data.Incomes)
        {
            var amount = income.AmountForMonth(year, monthNumber);
            if (amount > 0m)
            {
                var (account, pot) = data.Level(income.AccountId);
                entries.Add((account, pot, new CashFlowEntry(CashFlowKindDto.Income, income.Id, income.Name, pot, Values.RoundForDisplay(amount)), amount));
            }
        }

        foreach (var outgoing in data.Outgoings)
        {
            var amount = outgoing.MonthlyEquivalentForMonth(year, monthNumber);
            if (amount > 0m)
            {
                var (account, pot) = data.Level(outgoing.AccountId);
                entries.Add((account, pot, new CashFlowEntry(CashFlowKindDto.Outgoing, outgoing.Id, outgoing.Name, pot, Values.RoundForDisplay(amount)), amount));
            }
        }

        foreach (var transfer in data.Transfers)
        {
            var amount = transfer.MonthlyEquivalentForMonth(year, monthNumber);
            if (amount > 0m)
            {
                AddTransfer(entries, data, transfer.Id, transfer.Name, transfer.FromAccountId, transfer.ToAccountId, amount);
            }
        }

        var accounts = Ordered(entries.Select(e => e.Account), data)
            .Select(a =>
            {
                var mine = entries.Where(e => e.Account == a.Id).ToList();
                return new CashFlowMonthAccount(
                    a.Id,
                    a.Name,
                    mine.Where(e => e.Entry.IsIn).Select(e => e.Entry).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
                    mine.Where(e => !e.Entry.IsIn).Select(e => e.Entry).OrderByDescending(e => e.Amount).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
                    Values.RoundForDisplay(mine.Where(e => e.Entry.IsIn).Sum(e => e.Exact)),
                    Values.RoundForDisplay(mine.Where(e => !e.Entry.IsIn).Sum(e => e.Exact)));
            })
            .ToList();

        return Results.Ok(new CashFlowMonthResponse(year, monthNumber, data.Currency, accounts));
    }

    private static async Task<IResult> DaysAsync(Guid householdId, DateOnly? from, DateOnly? to, BudgetDbContext db)
    {
        if (from is not { } start || to is not { } end || end < start || end.DayNumber - start.DayNumber >= MaxDays)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["from"] = [$"Choose a period of up to {MaxDays} days."] });
        }

        var data = await LoadAsync(db, householdId);
        var entries = new List<(Guid? Account, string? Pot, CashFlowEntry Entry, DateOnly Date)>();

        foreach (var income in data.Incomes)
        {
            var (account, pot) = data.Level(income.AccountId);
            entries.AddRange(income.PaymentsBetween(start, end)
                .Select(p => (account, pot, new CashFlowEntry(CashFlowKindDto.Income, income.Id, income.Name, pot, p.Amount), p.Date)));
        }

        foreach (var outgoing in data.Outgoings)
        {
            var (account, pot) = data.Level(outgoing.AccountId);
            entries.AddRange(outgoing.PaymentsBetween(start, end)
                .Select(p => (account, pot, new CashFlowEntry(CashFlowKindDto.Outgoing, outgoing.Id, outgoing.Name, pot, p.Amount), p.Date)));
        }

        foreach (var transfer in data.Transfers)
        {
            foreach (var payment in transfer.PaymentsBetween(start, end))
            {
                var legs = new List<(Guid? Account, string? Pot, CashFlowEntry Entry, decimal Exact)>();
                AddTransfer(legs, data, transfer.Id, transfer.Name, transfer.FromAccountId, transfer.ToAccountId, payment.Amount);
                entries.AddRange(legs.Select(l => (l.Account, l.Pot, l.Entry, payment.Date)));
            }
        }

        var accounts = Ordered(entries.Select(e => e.Account), data)
            .Select(g =>
            {
                var running = 0m;
                var days = entries.Where(e => e.Account == g.Id)
                    .GroupBy(e => e.Date)
                    .OrderBy(d => d.Key)
                    .Select(d =>
                    {
                        var dayIn = d.Where(e => e.Entry.IsIn).Sum(e => e.Entry.Amount);
                        var dayOut = d.Where(e => !e.Entry.IsIn).Sum(e => e.Entry.Amount);
                        running += dayIn - dayOut;
                        var list = d.Select(e => e.Entry).OrderByDescending(e => e.IsIn).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                        return new CashFlowDay(d.Key, list, dayIn, dayOut, running, running < 0m);
                    })
                    .ToList();
                return new CashFlowDaysAccount(g.Id, g.Name, days, days.Sum(d => d.In), days.Sum(d => d.Out));
            })
            .ToList();

        return Results.Ok(new CashFlowDaysResponse(start, end, data.Currency, accounts));
    }

    /// <summary>
    /// A transfer leaves one account and arrives in another. Between an account and its own pots the money stays in
    /// the account, so it's left out.
    /// </summary>
    private static void AddTransfer(
        List<(Guid? Account, string? Pot, CashFlowEntry Entry, decimal Exact)> entries, Data data, Guid id, string? name, Guid fromId, Guid toId, decimal amount)
    {
        var (fromAccount, fromPot) = data.Level(fromId);
        var (toAccount, toPot) = data.Level(toId);
        if (fromAccount == toAccount)
        {
            return;
        }

        var shown = Values.RoundForDisplay(amount);
        entries.Add((toAccount, toPot, new CashFlowEntry(CashFlowKindDto.TransferIn, id, name ?? $"From {data.Name(fromAccount)}", toPot, shown), amount));
        entries.Add((fromAccount, fromPot, new CashFlowEntry(CashFlowKindDto.TransferOut, id, name ?? $"To {data.Name(toAccount)}", fromPot, shown), amount));
    }

    /// <summary>Each account with anything in it, by name, with "Not set" last.</summary>
    private static IEnumerable<(Guid? Id, string Name)> Ordered(IEnumerable<Guid?> accounts, Data data) =>
        accounts
            .Distinct()
            .Select(id => (Id: id, Name: data.Name(id)))
            .OrderBy(a => a.Id is null)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);

    private static async Task<Data> LoadAsync(BudgetDbContext db, Guid householdId)
    {
        var incomes = await db.IncomeStreams.Include(i => i.AmountChanges).Include(i => i.StatusPeriods).AsSplitQuery().AsNoTracking().ToListAsync();
        var outgoings = await db.Outgoings.Include(o => o.AmountChanges).Include(o => o.StatusPeriods).AsSplitQuery().AsNoTracking().ToListAsync();
        var transfers = await db.Transfers.Include(t => t.AmountChanges).Include(t => t.StatusPeriods).AsSplitQuery().AsNoTracking().ToListAsync();
        var accounts = await AccountDirectory.LoadAsync(db, householdId);
        var currency = await db.Households.Where(h => h.Id == householdId).Select(h => h.CurrencyCode).SingleAsync();
        return new Data(incomes, outgoings, transfers, accounts, currency);
    }

    private sealed record Data(
        IReadOnlyList<Budget.Domain.Income.IncomeStream> Incomes,
        IReadOnlyList<Outgoing> Outgoings,
        IReadOnlyList<Budget.Domain.Transfers.Transfer> Transfers,
        AccountDirectory Accounts,
        string Currency)
    {
        /// <summary>The account (not pot) an id belongs to, and the pot's name if it's a pot.</summary>
        public (Guid? Account, string? Pot) Level(Guid? id) => Accounts.Find(id) switch
        {
            null => (null, null),
            { ParentAccountId: { } parent } pot => (parent, pot.Name),
            Account account => (account.Id, null),
        };

        public string Name(Guid? accountId) => Accounts.Find(accountId)?.Name ?? "Not set";
    }
}
