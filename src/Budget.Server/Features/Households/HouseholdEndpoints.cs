using System.Security.Claims;
using Budget.Infrastructure.Persistence;
using Budget.Shared.Households;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Households;

public static class HouseholdEndpoints
{
    public static IEndpointRouteBuilder MapHouseholdEndpoints(this IEndpointRouteBuilder app)
    {
        // Every endpoint in this group is household-scoped: give each one a household policy.
        var household = app.MapGroup(HouseholdRoutes.Base);

        household.MapGet("/", GetHouseholdAsync).RequireAuthorization(HouseholdPolicies.Viewer);

        return app;
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
