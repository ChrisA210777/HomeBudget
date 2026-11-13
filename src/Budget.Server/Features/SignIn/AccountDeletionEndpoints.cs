using System.Security.Claims;
using Budget.Domain.Households;
using Budget.Infrastructure.Identity;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Activity;
using Budget.Server.Features.Households;
using Budget.Shared;
using Budget.Shared.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.SignIn;

/// <summary>
/// Deleting your own account (BUD-151, BUD-113, ADR-016, UK GDPR right to erasure). Straight away, in one transaction:
/// <list type="bullet">
/// <item>households you own that only you are in are deleted, with everything in them;</item>
/// <item>households you own that others use pass to the longest-standing other Admin, or the longest-standing member,
/// who's made an Admin;</item>
/// <item>you leave every other household; the person linked to you stays, unlinked, with its income and outgoings, so
/// the household's budget is intact;</item>
/// <item>recent activity, and invitations you sent, show "A former member" instead of your name; pending invitations
/// you sent, or that were sent to your email, are deleted;</item>
/// <item>your user record, sign-in links and settings are deleted, and you're signed out.</item>
/// </list>
/// </summary>
public static class AccountDeletionEndpoints
{
    /// <summary>Shown instead of the name of someone who has deleted their account, at the start of a sentence.</summary>
    public const string FormerMember = "A former member";

    /// <summary>The same, within a sentence: "Recorded by a former member on 1 Nov 2026".</summary>
    public const string FormerMemberInText = "a former member";

    public static IEndpointRouteBuilder MapAccountDeletionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(AccountRoutes.AccountDeletion, CheckAsync).RequireAuthorization();
        app.MapPost(AccountRoutes.DeleteAccount, DeleteAsync).RequireAuthorization();
        return app;
    }

    private static async Task<AccountDeletionCheck> CheckAsync(ClaimsPrincipal principal, BudgetDbContext db) =>
        await WhatDeletingDoesAsync(UserId(principal), db);

    private static async Task<AccountDeletionCheck> WhatDeletingDoesAsync(Guid userId, BudgetDbContext db)
    {
        var ownedIds = await db.HouseholdMembers.Where(m => m.UserId == userId && m.IsOwner).Select(m => m.HouseholdId).ToListAsync();
        // Household names are encrypted, so they're sorted here (BUD-166).
        var owned = (await db.Households.Include(h => h.Members).Where(h => ownedIds.Contains(h.Id)).ToListAsync())
            .OrderBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var toDelete = new List<OwnedHousehold>();
        var toPassOn = new List<PassedOnHousehold>();
        foreach (var household in owned)
        {
            if (household.NextOwner(userId) is { } next)
            {
                var name = await HouseholdEndpoints.MemberNameAsync(db, household.Id, next.UserId);
                toPassOn.Add(new PassedOnHousehold(household.Id, household.Name, name, next.Role != HouseholdRole.Admin));
            }
            else
            {
                toDelete.Add(new OwnedHousehold(household.Id, household.Name));
            }
        }

        var memberships = await db.HouseholdMembers.CountAsync(m => m.UserId == userId);
        return new AccountDeletionCheck(toDelete, toPassOn, memberships - owned.Count);
    }

    private static async Task<IResult> DeleteAsync(
        DeleteAccountRequest request,
        ClaimsPrincipal principal,
        BudgetDbContext db,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn)
    {
        if (!DeleteAccountRequest.IsConfirmed(request.Confirmation))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(DeleteAccountRequest.Confirmation)] = [$"Type {DeleteAccountRequest.Expected} to confirm."],
            });
        }

        var userId = UserId(principal);
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var check = await WhatDeletingDoesAsync(userId, db);

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();

            foreach (var household in check.HouseholdsToDelete)
            {
                await HouseholdEndpoints.DeleteHouseholdContentsAsync(db, household.HouseholdId);
            }

            // Households others use pass to someone else (ADR-016), and their members see who and why.
            foreach (var passed in check.HouseholdsToPassOn)
            {
                var household = await db.Households.Include(h => h.Members).SingleAsync(h => h.Id == passed.HouseholdId);
                if (household.PassOwnershipOn(userId) is { } passedTo)
                {
                    var name = await HouseholdEndpoints.MemberNameAsync(db, household.Id, passedTo.NewOwner.UserId);
                    if (passedTo.MadeAdmin)
                    {
                        db.ActivityEntries.Add(ActivityMessages.MemberRoleChanged(household.Id, name, HouseholdRole.Admin));
                    }

                    db.ActivityEntries.Add(ActivityMessages.OwnershipTransferred(household.Id, name));
                }
            }

            // Leaving the other households: their members see that someone left, and the person linked to you stays.
            var others = await db.HouseholdMembers.Where(m => m.UserId == userId).Select(m => m.HouseholdId).ToListAsync();
            foreach (var householdId in others)
            {
                db.ActivityEntries.Add(ActivityMessages.MemberLeft(householdId));
            }

            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await db.People.IgnoreQueryFilters()
                .Where(p => p.LinkedUserId == userId)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.LinkedUserId, (Guid?)null));
            await db.HouseholdMembers.Where(m => m.UserId == userId).ExecuteDeleteAsync();

            // Invitations: pending ones from you or to you go; the rest keep their history without your name.
            var email = user.Email;
            await db.Invitations.IgnoreQueryFilters()
                .Where(i => i.AcceptedAt == null && i.RevokedAt == null
                    && (i.InvitedByUserId == userId || (email != null && i.Email == email)))
                .ExecuteDeleteAsync();
            await db.Invitations.IgnoreQueryFilters()
                .Where(i => i.InvitedByUserId == userId)
                .ExecuteUpdateAsync(i => i.SetProperty(x => x.InvitedByName, FormerMember));

            // Recent activity keeps what happened, but not who you were.
            await db.ActivityEntries.IgnoreQueryFilters()
                .Where(a => a.UserId == userId)
                .ExecuteUpdateAsync(a => a
                    .SetProperty(x => x.ActorName, FormerMember)
                    .SetProperty(x => x.UserId, (Guid?)null));

            // So does the data download log (BUD-141): it then shows "A former member".
            await db.DataDownloads.IgnoreQueryFilters()
                .Where(d => d.UserId == userId)
                .ExecuteUpdateAsync(d => d.SetProperty(x => x.UserId, (Guid?)null));

            // Your user record, with its sign-in links and settings.
            var result = await users.DeleteAsync(user);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("The account couldn't be deleted: " + string.Join(", ", result.Errors.Select(e => e.Code)));
            }

            await transaction.CommitAsync();
        });

        await signIn.SignOutAsync();
        return Results.NoContent();
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
