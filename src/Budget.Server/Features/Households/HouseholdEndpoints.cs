using System.Security.Claims;
using Budget.Domain.Households;
using Budget.Domain.Outgoings;
using Budget.Domain.People;
using Budget.Infrastructure.Persistence;
using Budget.Shared.Households;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Households;

public static class HouseholdEndpoints
{
    public static IEndpointRouteBuilder MapHouseholdEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(HouseholdRoutes.Households, CreateAsync).RequireAuthorization();

        // Every endpoint in this group is household-scoped: give each one a household policy.
        var household = app.MapGroup(HouseholdRoutes.Base);

        household.MapGet("/", GetHouseholdAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        household.MapPut("/", RenameAsync).RequireAuthorization(HouseholdPolicies.Admin);
        household.MapDelete("/", DeleteAsync).RequireAuthorization(HouseholdPolicies.Owner);
        household.MapGet("/setup", GetSetupAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        household.MapGet("/members", GetMembersAsync).RequireAuthorization(HouseholdPolicies.Viewer);
        household.MapPut("/members/{userId:guid}/role", ChangeRoleAsync).RequireAuthorization(HouseholdPolicies.Admin);
        household.MapDelete("/members/{userId:guid}", RemoveMemberAsync).RequireAuthorization(HouseholdPolicies.Admin);
        household.MapDelete("/membership", LeaveAsync).RequireAuthorization(HouseholdPolicies.Viewer);

        return app;
    }

    /// <summary>The household's members: owner first, then by name.</summary>
    private static async Task<IReadOnlyList<HouseholdMemberSummary>> GetMembersAsync(Guid householdId, BudgetDbContext db)
    {
        var members = await (
                from member in db.HouseholdMembers
                join user in db.Users on member.UserId equals user.Id
                where member.HouseholdId == householdId
                orderby member.IsOwner descending, user.DisplayName
                select new { member.UserId, user.DisplayName, user.Email, member.Role, member.IsOwner, member.JoinedAt })
            .AsNoTracking()
            .ToListAsync();

        return members
            .Select(m => new HouseholdMemberSummary(m.UserId, m.DisplayName, m.Email, m.Role.ToString(), m.IsOwner, m.JoinedAt))
            .ToList();
    }

    /// <summary>Counts for the home page's getting-started prompts (BUD-68), including items not started yet or ended.</summary>
    private static async Task<HouseholdSetupResponse> GetSetupAsync(Guid householdId, BudgetDbContext db) =>
        new(
            await db.IncomeStreams.CountAsync(i => i.HouseholdId == householdId),
            await db.Outgoings.CountAsync(o => o.HouseholdId == householdId),
            await db.People.CountAsync(p => p.HouseholdId == householdId));

    /// <summary>
    /// Creates another household (BUD-21), set up as on sign-up: the caller is its owner and an Admin, with a
    /// person linked to their login and the default outgoing categories. It becomes their active household.
    /// </summary>
    private static async Task<IResult> CreateAsync(
        CreateHouseholdRequest request,
        ClaimsPrincipal principal,
        BudgetDbContext db,
        TimeProvider time)
    {
        var errors = HouseholdValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.SingleAsync(u => u.Id == userId);

        var household = Household.Create(userId, time.GetUtcNow(), request.Name);
        db.Households.Add(household);
        db.People.Add(Person.CreateForUser(household.Id, userId, user.DisplayName));
        db.OutgoingCategories.AddRange(OutgoingCategory.CreateDefaults(household.Id));
        user.LastActiveHouseholdId = household.Id;
        await db.SaveChangesAsync();

        return Results.Created(
            HouseholdRoutes.Household(household.Id),
            new HouseholdResponse(household.Id, household.Name, household.CurrencyCode, nameof(HouseholdRole.Admin), IsOwner: true, MemberCount: 1));
    }

    /// <summary>
    /// Deletes the household and everything in it (BUD-22), in one transaction. Members who had it open are
    /// moved to another of their households the next time the app loads who they are.
    /// </summary>
    private static async Task<IResult> DeleteAsync(Guid householdId, BudgetDbContext db)
    {
        // Children first: income and outgoings refer to people and categories without cascading.
        // Their own history (amounts, pauses, corrections) cascades in the database.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();

            await db.Users
                .Where(u => u.LastActiveHouseholdId == householdId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.LastActiveHouseholdId, (Guid?)null));
            await db.Outgoings.IgnoreQueryFilters().Where(o => o.HouseholdId == householdId).ExecuteDeleteAsync();
            await db.Accounts.IgnoreQueryFilters().Where(a => a.HouseholdId == householdId && a.ParentAccountId != null).ExecuteDeleteAsync();
            await db.Accounts.IgnoreQueryFilters().Where(a => a.HouseholdId == householdId).ExecuteDeleteAsync();
            await db.IncomeStreams.IgnoreQueryFilters().Where(i => i.HouseholdId == householdId).ExecuteDeleteAsync();
            await db.OutgoingCategories.IgnoreQueryFilters().Where(c => c.HouseholdId == householdId).ExecuteDeleteAsync();
            await db.People.IgnoreQueryFilters().Where(p => p.HouseholdId == householdId).ExecuteDeleteAsync();
            await db.HouseholdMembers.Where(m => m.HouseholdId == householdId).ExecuteDeleteAsync();
            await db.Households.Where(h => h.Id == householdId).ExecuteDeleteAsync();

            await transaction.CommitAsync();
        });

        return Results.NoContent();
    }

    private static async Task<IResult> ChangeRoleAsync(Guid householdId, Guid userId, ChangeRoleRequest request, BudgetDbContext db)
    {
        if (!Enum.TryParse<HouseholdRole>(request.Role, ignoreCase: false, out var role) || !Enum.IsDefined(role))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(ChangeRoleRequest.Role)] = ["Choose Viewer, User or Admin."],
            });
        }

        var household = await db.Households.Include(h => h.Members).SingleAsync(h => h.Id == householdId);
        if (household.Members.All(m => m.UserId != userId))
        {
            return Results.NotFound();
        }

        try
        {
            household.ChangeRole(userId, role);
        }
        catch (HouseholdMembershipException ex)
        {
            return Refused(ex);
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static Task<IResult> RemoveMemberAsync(Guid householdId, Guid userId, BudgetDbContext db) =>
        RemoveAsync(householdId, userId, db);

    private static Task<IResult> LeaveAsync(Guid householdId, ClaimsPrincipal principal, BudgetDbContext db) =>
        RemoveAsync(householdId, Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!), db);

    /// <summary>
    /// Removes a member. Their linked person stays, with its income and outgoings, but is no longer linked
    /// to their login. If this was their active household, they're moved to another of theirs.
    /// </summary>
    private static async Task<IResult> RemoveAsync(Guid householdId, Guid userId, BudgetDbContext db)
    {
        var household = await db.Households.Include(h => h.Members).SingleAsync(h => h.Id == householdId);
        if (household.Members.All(m => m.UserId != userId))
        {
            return Results.NotFound();
        }

        try
        {
            household.RemoveMember(userId);
        }
        catch (HouseholdMembershipException ex)
        {
            return Refused(ex);
        }

        foreach (var person in await db.People.Where(p => p.HouseholdId == householdId && p.LinkedUserId == userId).ToListAsync())
        {
            person.Unlink();
        }

        var user = await db.Users.SingleAsync(u => u.Id == userId);
        if (user.LastActiveHouseholdId == householdId)
        {
            user.LastActiveHouseholdId = await db.HouseholdMembers
                .Where(m => m.UserId == userId && m.HouseholdId != householdId)
                .Join(db.Households, m => m.HouseholdId, h => h.Id, (_, h) => h)
                .OrderBy(h => h.Name)
                .Select(h => (Guid?)h.Id)
                .FirstOrDefaultAsync();
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static IResult Refused(HouseholdMembershipException ex) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);

    private static async Task<IResult> RenameAsync(Guid householdId, RenameHouseholdRequest request, ClaimsPrincipal principal, BudgetDbContext db)
    {
        var errors = HouseholdValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var household = await db.Households.SingleAsync(h => h.Id == householdId);
        if (request.PreviousName is { } previous && !string.Equals(previous, household.Name, StringComparison.Ordinal))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Someone else has renamed this household.",
                detail: $"It's now called \"{household.Name}\".");
        }

        household.Rename(request.Name);
        await db.SaveChangesAsync();

        return await GetHouseholdAsync(householdId, principal, db);
    }

    private static async Task<IResult> GetHouseholdAsync(Guid householdId, ClaimsPrincipal principal, BudgetDbContext db)
    {
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var response = await db.Households
            .AsNoTracking()
            .Where(h => h.Id == householdId)
            .Select(h => new
            {
                h.Id,
                h.Name,
                h.CurrencyCode,
                Me = h.Members.Where(m => m.UserId == userId).Select(m => new { m.Role, m.IsOwner }).FirstOrDefault(),
                MemberCount = h.Members.Count,
            })
            .SingleAsync();

        // The authorisation check has already confirmed the caller is a member.
        var me = response.Me!;
        return Results.Ok(new HouseholdResponse(
            response.Id,
            response.Name,
            response.CurrencyCode,
            me.Role.ToString(),
            me.IsOwner,
            response.MemberCount));
    }
}
