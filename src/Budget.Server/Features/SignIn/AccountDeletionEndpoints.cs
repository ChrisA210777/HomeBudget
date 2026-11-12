using System.Security.Claims;
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
/// Deleting your own account (BUD-151, UK GDPR right to erasure). Straight away, in one transaction:
/// <list type="bullet">
/// <item>households you own that only you are in are deleted, with everything in them;</item>
/// <item>you leave every other household; the person linked to you stays, unlinked, with its income and outgoings, so
/// the household's budget is intact;</item>
/// <item>recent activity, and invitations you sent, show "A former member" instead of your name; pending invitations
/// you sent, or that were sent to your email, are deleted;</item>
/// <item>your user record, sign-in links and settings are deleted, and you're signed out.</item>
/// </list>
/// Households you own that have other members must be handed to another Admin (BUD-157) or deleted first.
/// </summary>
public static class AccountDeletionEndpoints
{
    /// <summary>Shown instead of the name of someone who has deleted their account.</summary>
    public const string FormerMember = "A former member";

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
        var owned = await db.HouseholdMembers
            .Where(m => m.UserId == userId && m.IsOwner)
            .Join(db.Households, m => m.HouseholdId, h => h.Id, (m, h) => new
            {
                h.Id,
                h.Name,
                OtherMembers = db.HouseholdMembers.Count(o => o.HouseholdId == h.Id && o.UserId != userId),
            })
            .OrderBy(h => h.Name)
            .ToListAsync();
        var memberships = await db.HouseholdMembers.CountAsync(m => m.UserId == userId);

        return new AccountDeletionCheck(
            owned.Where(h => h.OtherMembers == 0).Select(h => new OwnedHousehold(h.Id, h.Name, 0)).ToList(),
            owned.Where(h => h.OtherMembers > 0).Select(h => new OwnedHousehold(h.Id, h.Name, h.OtherMembers)).ToList(),
            memberships - owned.Count);
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
        if (!check.CanDelete)
        {
            var names = string.Join(", ", check.HouseholdsToHandOn.Select(h => h.Name));
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "You own households that other people use.",
                detail: $"Make another Admin the owner of {names}, or delete it, first.");
        }

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();

            foreach (var household in check.HouseholdsToDelete)
            {
                await HouseholdEndpoints.DeleteHouseholdContentsAsync(db, household.HouseholdId);
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
