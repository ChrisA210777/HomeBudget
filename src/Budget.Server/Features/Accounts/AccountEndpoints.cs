using Budget.Domain.Accounts;
using Budget.Infrastructure.Persistence;
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

        AccountSummary Summary(Account a) => new(
            a.Id,
            a.Name,
            a.Provider,
            a.OwnerPersonId,
            a.OwnerPersonId is { } owner ? people.GetValueOrDefault(owner) : null,
            a.ParentAccountId,
            a.IsArchived,
            directory.DisplayName(a.Id)!,
            counts.GetValueOrDefault(a.Id));

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
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        // A pot belongs to its account's owner; an account's pots follow it.
        var owner = directory.Find(account.ParentAccountId) is { } parent ? parent.OwnerPersonId : request.OwnerPersonId;
        account.Update(request.Name, owner, request.Provider);
        foreach (var pot in directory.All.Where(p => p.ParentAccountId == account.Id))
        {
            pot.FollowOwner(owner);
        }

        await db.SaveChangesAsync();
        return Results.Ok((await SummariesAsync(householdId, db)).Single(a => a.Id == account.Id));
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

        await db.SaveChangesAsync();
        return Results.Ok(await SummariesAsync(householdId, db));
    }

    /// <summary>
    /// Moves every outgoing to the other account or pot, then archives this one. Merging an account moves its pots
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
