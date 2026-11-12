using System.Security.Claims;
using Budget.Domain.Households;
using Budget.Domain.People;
using Budget.Infrastructure.Identity;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Activity;
using Budget.Server.Hosting;
using Budget.Shared.Households;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Households;

/// <summary>
/// Invitations to join a household (ADR-015). Admins create, list and revoke them; anyone signed in who has a
/// link can see what it's for and accept it, unless it asks for a matching email they haven't signed in with.
/// </summary>
public static class InvitationEndpoints
{
    private const string ProtectorPurpose = "Budget.Invitations.Token";

    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var household = app.MapGroup(HouseholdRoutes.Base + "/invitations");
        household.MapGet("/", ListAsync).RequireAuthorization(HouseholdPolicies.Admin);
        household.MapPost("/", CreateAsync).RequireAuthorization(HouseholdPolicies.Admin);
        household.MapDelete("/{invitationId:guid}", RevokeAsync).RequireAuthorization(HouseholdPolicies.Admin);

        app.MapGet("/api/invitations/{token}", PreviewAsync).RequireAuthorization().RequireRateLimiting(RateLimits.Invitations);
        app.MapPost("/api/invitations/{token}/accept", AcceptAsync).RequireAuthorization().RequireRateLimiting(RateLimits.Invitations);
        return app;
    }

    private static async Task<IResult> ListAsync(Guid householdId, HttpContext http, BudgetDbContext db, IDataProtectionProvider protection, TimeProvider time)
    {
        var now = time.GetUtcNow();

        // Expired invitations forget who they were for (ADR-015).
        await db.Invitations
            .Where(i => i.HouseholdId == householdId && i.Email != null && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Email, (string?)null));

        var pending = await db.Invitations
            .Where(i => i.HouseholdId == householdId && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderBy(i => i.SentAt)
            .ToListAsync();
        var people = await db.People.IgnoreQueryFilters().Where(p => p.HouseholdId == householdId).ToDictionaryAsync(p => p.Id, p => p.DisplayName);
        var protector = protection.CreateProtector(ProtectorPurpose);

        return Results.Ok(pending.Select(i => Summary(i, people, Link(http, protector, i))).ToList());
    }

    private static async Task<IResult> CreateAsync(
        Guid householdId,
        CreateInvitationRequest request,
        ClaimsPrincipal principal,
        HttpContext http,
        UserManager<ApplicationUser> users,
        BudgetDbContext db,
        IDataProtectionProvider protection,
        TimeProvider time)
    {
        var now = time.GetUtcNow();
        var errors = new Dictionary<string, string[]>();

        string? email = null;
        try
        {
            email = Invitation.NormaliseEmail(request.Email ?? string.Empty);
        }
        catch (ArgumentException ex)
        {
            errors[nameof(request.Email)] = [ex.Message];
        }

        if (!Enum.TryParse<HouseholdRole>(request.Role, ignoreCase: false, out var role) || !Enum.IsDefined(role))
        {
            errors[nameof(request.Role)] = ["Choose Viewer, User or Admin."];
        }

        Person? person = null;
        if (request.PersonId is { } personId)
        {
            person = await db.People.IgnoreQueryFilters().SingleOrDefaultAsync(p => p.Id == personId && p.HouseholdId == householdId);
            if (person is null || person.LinkedUserId is not null)
            {
                errors[nameof(request.PersonId)] = ["Choose someone who isn't a member yet, or someone new."];
            }
            else if (await db.Invitations.AnyAsync(i => i.HouseholdId == householdId && i.PersonId == personId && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now))
            {
                errors[nameof(request.PersonId)] = [$"There's already an invitation for {person.DisplayName}. Revoke it to send a new one."];
            }
        }

        if (email is not null && await (
                from member in db.HouseholdMembers
                join user in db.Users on member.UserId equals user.Id
                where member.HouseholdId == householdId && user.NormalizedEmail == email.ToUpperInvariant()
                select member).AnyAsync())
        {
            errors[nameof(request.Email)] = ["They're already a member of this household."];
        }

        if (await db.Invitations.CountAsync(i => i.HouseholdId == householdId && i.SentAt > now.AddDays(-1)) >= Invitation.MaxPerDay)
        {
            errors[nameof(request.Email)] = [$"You can send up to {Invitation.MaxPerDay} invitations a day. Try again tomorrow."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var inviter = (await users.GetUserAsync(principal))!;
        var protector = protection.CreateProtector(ProtectorPurpose);
        var (invitation, token) = Invitation.Create(
            householdId, email!, role, person?.Id, request.RequireEmailMatch, inviter.Id, inviter.DisplayName, now, protector.Protect);
        db.Invitations.Add(invitation);

        // The email isn't kept in recent activity: it's only kept while the invitation is pending (ADR-015).
        db.ActivityEntries.Add(ActivityMessages.MemberInvited(householdId, person?.DisplayName ?? "someone new", role));
        await db.SaveChangesAsync();

        var people = person is null ? new Dictionary<Guid, string>() : new Dictionary<Guid, string> { [person.Id] = person.DisplayName };
        return Results.Created(InvitationRoutes.Invitation(householdId, invitation.Id), Summary(invitation, people, AbsoluteLink(http, token)));
    }

    private static async Task<IResult> RevokeAsync(Guid householdId, Guid invitationId, BudgetDbContext db, TimeProvider time)
    {
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Id == invitationId && i.HouseholdId == householdId);
        if (invitation is null)
        {
            return Results.NotFound();
        }

        try
        {
            invitation.Revoke(time.GetUtcNow());
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Can't revoke this invitation", detail: ex.Message);
        }

        var invited = invitation.PersonId is { } personId
            ? await db.People.IgnoreQueryFilters().Where(p => p.Id == personId).Select(p => p.DisplayName).FirstOrDefaultAsync()
            : null;
        db.ActivityEntries.Add(ActivityMessages.InvitationCancelled(householdId, invited ?? "someone new"));
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> PreviewAsync(string token, ClaimsPrincipal principal, UserManager<ApplicationUser> users, BudgetDbContext db, TimeProvider time)
    {
        var invitation = await FindAsync(db, token);
        if (invitation is null)
        {
            return Results.NotFound();
        }

        var user = (await users.GetUserAsync(principal))!;
        var household = await db.Households.Where(h => h.Id == invitation.HouseholdId).Select(h => h.Name).SingleAsync();
        var personName = invitation.PersonId is { } personId
            ? await db.People.IgnoreQueryFilters().Where(p => p.Id == personId).Select(p => p.DisplayName).SingleOrDefaultAsync()
            : null;
        var member = await db.HouseholdMembers.AnyAsync(m => m.HouseholdId == invitation.HouseholdId && m.UserId == user.Id);

        return Results.Ok(new InvitationPreview(
            household,
            invitation.InvitedByName,
            invitation.Role.ToString(),
            personName,
            invitation.ExpiresAt,
            invitation.StatusAt(time.GetUtcNow()).ToString(),
            invitation.RequireEmailMatch,
            invitation.AllowsEmail(user.Email),
            invitation.RequireEmailMatch ? invitation.Email : null,
            user.Email,
            member));
    }

    private static async Task<IResult> AcceptAsync(string token, ClaimsPrincipal principal, UserManager<ApplicationUser> users, BudgetDbContext db, TimeProvider time)
    {
        var invitation = await FindAsync(db, token);
        if (invitation is null)
        {
            return Results.NotFound();
        }

        var now = time.GetUtcNow();
        var userId = (await users.GetUserAsync(principal))!.Id;
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        if (!invitation.AllowsEmail(user.Email))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Wrong account",
                detail: "This invitation is for a different email address. Sign in with the account it was sent to.");
        }

        var household = await db.Households.Include(h => h.Members).SingleAsync(h => h.Id == invitation.HouseholdId);
        try
        {
            invitation.Accept(user.Id, now);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Can't use this invitation", detail: ex.Message);
        }

        if (household.Members.All(m => m.UserId != user.Id))
        {
            household.AddMember(user.Id, invitation.Role, now);
            db.ActivityEntries.Add(ActivityMessages.MemberJoined(household.Id, invitation.Role));

            // They become the Person they were invited as, or someone new (BUD-44).
            var person = invitation.PersonId is { } personId
                ? await db.People.IgnoreQueryFilters().SingleOrDefaultAsync(p => p.Id == personId && p.HouseholdId == household.Id && p.LinkedUserId == null)
                : null;
            if (person is not null)
            {
                person.LinkTo(user.Id);
            }
            else
            {
                db.People.Add(Person.CreateForUser(household.Id, user.Id, user.DisplayName));
            }
        }

        user.LastActiveHouseholdId = household.Id;
        await db.SaveChangesAsync();

        // The empty household made for them when they first signed in isn't needed now.
        foreach (var unused in await UnusedStarterHouseholdsAsync(db, user, household.Id))
        {
            await HouseholdEndpoints.DeleteHouseholdAsync(db, unused);
        }

        return Results.Ok(new AcceptInvitationResponse(household.Id, household.Name));
    }

    /// <summary>
    /// Households created with the user's login (within a minute of it) that only they belong to and that have nothing
    /// in them: no outgoings, income, accounts or transfers.
    /// </summary>
    private static async Task<List<Guid>> UnusedStarterHouseholdsAsync(BudgetDbContext db, ApplicationUser user, Guid joined)
    {
        var from = user.CreatedAt.AddMinutes(-1);
        var to = user.CreatedAt.AddMinutes(1);
        return await db.Households
            .Where(h => h.Id != joined && h.CreatedAt >= from && h.CreatedAt <= to)
            .Where(h => db.HouseholdMembers.Count(m => m.HouseholdId == h.Id) == 1 && db.HouseholdMembers.Any(m => m.HouseholdId == h.Id && m.UserId == user.Id && m.IsOwner))
            .Where(h => !db.Outgoings.IgnoreQueryFilters().Any(o => o.HouseholdId == h.Id)
                && !db.IncomeStreams.IgnoreQueryFilters().Any(i => i.HouseholdId == h.Id)
                && !db.Accounts.IgnoreQueryFilters().Any(a => a.HouseholdId == h.Id)
                && !db.Transfers.IgnoreQueryFilters().Any(t => t.HouseholdId == h.Id))
            .Select(h => h.Id)
            .ToListAsync();
    }

    private static Task<Invitation?> FindAsync(BudgetDbContext db, string token)
    {
        var hash = Invitation.Hash(token);
        return db.Invitations.SingleOrDefaultAsync(i => i.TokenHash == hash);
    }

    private static InvitationSummary Summary(Invitation invitation, IReadOnlyDictionary<Guid, string> people, string? link) => new(
        invitation.Id,
        invitation.Email ?? string.Empty,
        invitation.Role.ToString(),
        invitation.PersonId,
        invitation.PersonId is { } id ? people.GetValueOrDefault(id) : null,
        invitation.RequireEmailMatch,
        invitation.InvitedByName,
        invitation.ExpiresAt,
        link);

    private static string? Link(HttpContext http, IDataProtector protector, Invitation invitation)
    {
        try
        {
            return AbsoluteLink(http, protector.Unprotect(invitation.ProtectedToken));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // The app's keys have changed since it was sent; it can be revoked and sent again.
            return null;
        }
    }

    private static string AbsoluteLink(HttpContext http, string token) =>
        $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}{InvitationRoutes.Page(token)}";
}
