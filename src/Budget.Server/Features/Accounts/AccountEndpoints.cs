using Budget.Domain.Accounts;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Activity;
using Budget.Server.Features.Households;
using Budget.Shared.Accounts;
using Budget.Shared.Households;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Accounts;

/// <summary>
/// Accounts and pots (BUD-89, ADR-014). Anyone in the household can see them; Users and Admins can add, change,
/// archive and restore them; only Admins can merge. Accounts are never deleted.
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapHouseholdAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup(HouseholdRoutes.Base + "/accounts");
        accounts.MapGet("/", ListAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        accounts.MapPost("/", CreateAsync).RequireAuthorization(HouseholdPolicies.User);
        accounts.MapPut("/{accountId:guid}", UpdateAsync).RequireAuthorization(HouseholdPolicies.User);
        accounts.MapPost("/{accountId:guid}/archive", (Guid householdId, Guid accountId, BudgetDbContext db) => SetArchivedAsync(householdId, accountId, archived: true, db))
            .RequireAuthorization(HouseholdPolicies.User);
        accounts.MapPost("/{accountId:guid}/restore", (Guid householdId, Guid accountId, BudgetDbContext db) => SetArchivedAsync(householdId, accountId, archived: false, db))
            .RequireAuthorization(HouseholdPolicies.User);
        accounts.MapPost("/{accountId:guid}/merge", MergeAsync).RequireAuthorization(HouseholdPolicies.Admin);
        accounts.MapPut("/{accountId:guid}/month-start", SetMonthStartAsync).RequireAuthorization(HouseholdPolicies.User);
        return app;
    }

    private static async Task<IResult> ListAsync(Guid householdId, BudgetDbContext db) =>
        Results.Ok(await SummariesAsync(householdId, db));

    internal static async Task<IReadOnlyList<AccountSummary>> SummariesAsync(Guid householdId, BudgetDbContext db)
    {
        var directory = await AccountDirectory.LoadAsync(db, householdId);
        var people = await db.People.ToDictionaryAsync(p => p.Id, p => p.DisplayName);
        var counts = await db.Outgoings
            .Where(o => o.AccountId != null)
            .GroupBy(o => o.AccountId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var incomeCounts = await db.IncomeStreams
            .Where(s => s.AccountId != null)
            .GroupBy(s => s.AccountId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var incomes = await db.IncomeStreams.AsNoTracking().ToListAsync();
        var transfers = await db.Transfers.AsNoTracking().ToListAsync();

        AccountSummary Summary(Account a)
        {
            var options = MonthStarts.Options(a, directory, incomes, transfers);
            var chosen = options.FirstOrDefault(o => o.Id == a.MonthStartsWithId);
            return new(
                a.Id,
                a.Name,
                a.Provider,
                a.OwnerPersonId,
                a.OwnerPersonId is { } owner ? people.GetValueOrDefault(owner) : null,
                a.ParentAccountId,
                a.IsArchived,
                directory.DisplayName(a.Id)!,
                counts.GetValueOrDefault(a.Id),
                incomeCounts.GetValueOrDefault(a.Id))
            {
                MonthStartsWithId = chosen?.Id,
                MonthStartsWithName = chosen?.Name,
                MonthStartMissing = a.MonthStartsWithId is not null && chosen is null,
                MonthStartOptions = options,
            };
        }

        var byName = StringComparer.CurrentCultureIgnoreCase;
        return directory.All
            .Where(a => !a.IsPot)
            .OrderBy(a => a.Name, byName)
            .SelectMany(a => new[] { Summary(a) }.Concat(directory.All.Where(p => p.ParentAccountId == a.Id).OrderBy(p => p.Name, byName).Select(Summary)))
            .ToList();
    }

    private static async Task<IResult> CreateAsync(Guid householdId, SaveAccountRequest request, BudgetDbContext db)
    {
        var directory = await AccountDirectory.LoadAsync(db, householdId);
        var parent = directory.Find(request.ParentAccountId);
        var errors = await ValidateAsync(request, directory, request.ParentAccountId, exceptId: null, db);
        if (request.ParentAccountId is not null && (parent is null || parent.IsPot))
        {
            errors[nameof(request.ParentAccountId)] = ["Choose an account in this household for the pot."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var account = parent is null
            ? Account.Create(householdId, request.Name, request.OwnerPersonId, request.Provider)
            : Account.CreatePot(parent, request.Name);
        db.Accounts.Add(account);
        db.ActivityEntries.Add(ActivityMessages.AccountAdded(
            householdId, account.Id, parent is null ? account.Name : $"{parent.Name} › {account.Name}", isPot: parent is not null));
        await db.SaveChangesAsync();
        return Results.Created(HouseholdAccountRoutes.Account(householdId, account.Id), (await SummariesAsync(householdId, db)).Single(a => a.Id == account.Id));
    }

    private static async Task<IResult> UpdateAsync(Guid householdId, Guid accountId, SaveAccountRequest request, BudgetDbContext db)
    {
        var directory = await AccountDirectory.LoadAsync(db, householdId);
        if (directory.Find(accountId) is not { } account)
        {
            return Results.NotFound();
        }

        var errors = await ValidateAsync(request, directory, account.ParentAccountId, exceptId: account.Id, db);
        if (!account.IsPot && request.MonthStartsWithId is { } monthStart && !await IsMonthStartOptionAsync(account, monthStart, directory, db))
        {
            errors[nameof(request.MonthStartsWithId)] = [MonthStartProblem];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var monthStartChanged = !account.IsPot && account.MonthStartsWithId != request.MonthStartsWithId;
        if (!account.IsPot)
        {
            account.StartMonthWith(request.MonthStartsWithId);
        }

        // A pot belongs to its account's owner; an account's pots follow it.
        var owner = directory.Find(account.ParentAccountId) is { } parent ? parent.OwnerPersonId : request.OwnerPersonId;
        account.Update(request.Name, owner, request.Provider);
        foreach (var pot in directory.All.Where(p => p.ParentAccountId == account.Id))
        {
            pot.FollowOwner(owner);
        }

        var shownAs = directory.Find(account.ParentAccountId) is { } potOf ? $"{potOf.Name} › {account.Name}" : account.Name;
        db.ActivityEntries.Add(monthStartChanged
            ? ActivityMessages.AccountMonthStart(householdId, account.Id, shownAs, await PaymentNameAsync(request.MonthStartsWithId, directory, db))
            : ActivityMessages.AccountUpdated(householdId, account.Id, shownAs));
        await db.SaveChangesAsync();
        return Results.Ok((await SummariesAsync(householdId, db)).Single(a => a.Id == account.Id));
    }

    private const string MonthStartProblem = "Choose income or a transfer that's paid into this account.";

    /// <summary>When an account's month starts, e.g. from Cash flow's suggestion (BUD-136).</summary>
    private static async Task<IResult> SetMonthStartAsync(Guid householdId, Guid accountId, SetMonthStartRequest request, BudgetDbContext db)
    {
        var directory = await AccountDirectory.LoadAsync(db, householdId);
        if (directory.Find(accountId) is not { IsPot: false } account)
        {
            return Results.NotFound();
        }

        if (request.MonthStartsWithId is { } monthStart && !await IsMonthStartOptionAsync(account, monthStart, directory, db))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.MonthStartsWithId)] = [MonthStartProblem] });
        }

        account.StartMonthWith(request.MonthStartsWithId);
        db.ActivityEntries.Add(ActivityMessages.AccountMonthStart(
            householdId, account.Id, account.Name, await PaymentNameAsync(request.MonthStartsWithId, directory, db)));
        await db.SaveChangesAsync();
        return Results.Ok((await SummariesAsync(householdId, db)).Single(a => a.Id == account.Id));
    }

    /// <summary>The name of the income or transfer an account's month starts with, for recent activity.</summary>
    private static async Task<string?> PaymentNameAsync(Guid? paymentId, AccountDirectory directory, BudgetDbContext db)
    {
        if (paymentId is not { } id)
        {
            return null;
        }

        var income = await db.IncomeStreams.Where(i => i.Id == id).Select(i => i.Name).FirstOrDefaultAsync();
        return income ?? (await db.Transfers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id) is { } transfer ? MonthStarts.Name(transfer, directory) : null);
    }

    private static async Task<bool> IsMonthStartOptionAsync(Account account, Guid paymentId, AccountDirectory directory, BudgetDbContext db)
    {
        var incomes = await db.IncomeStreams.AsNoTracking().Where(i => i.Id == paymentId).ToListAsync();
        var transfers = await db.Transfers.AsNoTracking().Where(t => t.Id == paymentId).ToListAsync();
        return MonthStarts.Options(account, directory, incomes, transfers).Any(o => o.Id == paymentId);
    }

    private static async Task<IResult> SetArchivedAsync(Guid householdId, Guid accountId, bool archived, BudgetDbContext db)
    {
        var directory = await AccountDirectory.LoadAsync(db, householdId);
        if (directory.Find(accountId) is not { } account)
        {
            return Results.NotFound();
        }

        if (archived)
        {
            account.Archive();
        }
        else
        {
            account.Restore();
        }

        db.ActivityEntries.Add(ActivityMessages.AccountArchived(householdId, account.Id, directory.DisplayName(account.Id)!, archived));
        await db.SaveChangesAsync();
        return Results.Ok(await SummariesAsync(householdId, db));
    }

    /// <summary>
    /// Moves every outgoing and income to the other account or pot, then archives this one. Merging an account moves its pots
    /// too, combining any with the same name. An account with pots can't be merged into a pot.
    /// </summary>
    private static async Task<IResult> MergeAsync(Guid householdId, Guid accountId, MergeAccountRequest request, BudgetDbContext db)
    {
        var directory = await AccountDirectory.LoadAsync(db, householdId);
        if (directory.Find(accountId) is not { } source)
        {
            return Results.NotFound();
        }

        var target = directory.Find(request.IntoAccountId);
        var sourcePots = directory.All.Where(p => p.ParentAccountId == source.Id).ToList();
        string? problem = target is null ? "Choose an account or pot in this household to merge into."
            : target.Id == source.Id ? "Choose a different account or pot."
            : target.ParentAccountId == source.Id ? "An account can't be merged into one of its own pots."
            : target.IsPot && sourcePots.Count > 0 ? "An account with pots can only be merged into another account."
            : null;
        if (problem is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.IntoAccountId)] = [problem] });
        }

        var moves = new List<(Guid From, Guid To)> { (source.Id, target!.Id) };
        foreach (var pot in sourcePots)
        {
            if (directory.All.FirstOrDefault(p => p.ParentAccountId == target.Id && string.Equals(p.Name, pot.Name, StringComparison.CurrentCultureIgnoreCase)) is { } same)
            {
                moves.Add((pot.Id, same.Id));
                pot.Archive();
            }
            else
            {
                pot.MoveTo(target);
            }
        }

        // A transfer between the two would end up going from an account to itself (BUD-91).
        var map = moves.ToDictionary(m => m.From, m => m.To);
        Guid After(Guid id) => map.TryGetValue(id, out var to) ? to : id;
        var transfers = await db.Transfers.Select(t => new { t.FromAccountId, t.ToAccountId }).ToListAsync();
        if (transfers.Any(t => After(t.FromAccountId) == After(t.ToAccountId)))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.IntoAccountId)] = ["There's a transfer between these accounts. Delete it, or move it to another account, first."],
            });
        }

        db.ActivityEntries.Add(ActivityMessages.AccountMerged(householdId, target.Id, directory.DisplayName(source.Id)!, directory.DisplayName(target.Id)!));
        source.Archive();
        if (target.IsArchived)
        {
            target.Restore();
        }

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            foreach (var (from, to) in moves)
            {
                await db.Outgoings.Where(o => o.AccountId == from).ExecuteUpdateAsync(s => s.SetProperty(o => o.AccountId, to));
                await db.IncomeStreams.Where(i => i.AccountId == from).ExecuteUpdateAsync(s => s.SetProperty(i => i.AccountId, to));
                await db.Outgoings.Where(o => o.SavingsAccountId == from).ExecuteUpdateAsync(s => s.SetProperty(o => o.SavingsAccountId, to));
                await db.Transfers.Where(t => t.FromAccountId == from).ExecuteUpdateAsync(s => s.SetProperty(t => t.FromAccountId, to));
                await db.Transfers.Where(t => t.ToAccountId == from).ExecuteUpdateAsync(s => s.SetProperty(t => t.ToAccountId, to));
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        return Results.Ok(await SummariesAsync(householdId, db));
    }

    private static async Task<Dictionary<string, string[]>> ValidateAsync(
        SaveAccountRequest request, AccountDirectory directory, Guid? parentId, Guid? exceptId, BudgetDbContext db)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            errors[nameof(request.Name)] = ["Enter a name."];
        }
        else if (name.Length > Account.NameMaxLength)
        {
            errors[nameof(request.Name)] = [$"Keep it to {Account.NameMaxLength} characters."];
        }
        else if (name.Contains(Account.PotSeparator.Trim(), StringComparison.Ordinal))
        {
            errors[nameof(request.Name)] = ["A name can't contain ›, which shows a pot."];
        }
        else if (directory.NameTaken(name, parentId, exceptId))
        {
            errors[nameof(request.Name)] = [parentId is null ? "There's already an account with that name." : "That account already has a pot with that name."];
        }

        if (request.Provider?.Trim().Length > Account.ProviderMaxLength)
        {
            errors[nameof(request.Provider)] = [$"Keep it to {Account.ProviderMaxLength} characters."];
        }

        if (parentId is null && request.OwnerPersonId is { } owner && !await db.People.AnyAsync(p => p.Id == owner))
        {
            errors[nameof(request.OwnerPersonId)] = ["Choose someone in this household, or the household itself."];
        }

        return errors;
    }
}
