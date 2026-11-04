using System.Security.Claims;
using Budget.Domain.Households;
using Budget.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Features.Households;

/// <summary>
/// Policies for household-scoped endpoints. The household comes from the <c>{householdId}</c>
/// route value. See the Roles &amp; Permissions page for who can do what.
/// </summary>
public static class HouseholdPolicies
{
    /// <summary>Any member: Viewer, User or Admin.</summary>
    public const string Viewer = "HouseholdViewer";

    /// <summary>Members who can change budget data: User or Admin.</summary>
    public const string User = "HouseholdUser";

    /// <summary>Members who can manage the household and its members.</summary>
    public const string Admin = "HouseholdAdmin";

    /// <summary>Only the household's owner.</summary>
    public const string Owner = "HouseholdOwner";

    /// <summary>The route parameter that identifies the household.</summary>
    public const string RouteParameter = "householdId";

    public static IServiceCollection AddHouseholdAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Viewer, p => p.RequireAuthenticatedUser().AddRequirements(new HouseholdRequirement(HouseholdRole.Viewer)))
            .AddPolicy(User, p => p.RequireAuthenticatedUser().AddRequirements(new HouseholdRequirement(HouseholdRole.User)))
            .AddPolicy(Admin, p => p.RequireAuthenticatedUser().AddRequirements(new HouseholdRequirement(HouseholdRole.Admin)))
            .AddPolicy(Owner, p => p.RequireAuthenticatedUser().AddRequirements(new HouseholdRequirement(HouseholdRole.Admin, OwnerOnly: true)));

        services.AddScoped<IAuthorizationHandler, HouseholdMembershipHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, HouseholdAuthorizationResultHandler>();

        return services;
    }
}

/// <summary>The caller must be a member of the route's household with at least this role.</summary>
public sealed record HouseholdRequirement(HouseholdRole MinimumRole, bool OwnerOnly = false) : IAuthorizationRequirement;

/// <summary>
/// Checks the caller's membership of the household in the route. On success it sets the
/// request's current household, which the database query filters then use.
/// </summary>
public sealed class HouseholdMembershipHandler(BudgetDbContext db, CurrentHousehold currentHousehold)
    : AuthorizationHandler<HouseholdRequirement>
{
    /// <summary>The failure reason used when the caller isn't a member (or the household doesn't exist).</summary>
    public const string NotAMember = "Not a member of this household.";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, HouseholdRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!TryGetHouseholdId(context.Resource, out var householdId)
            || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            context.Fail(new AuthorizationFailureReason(this, NotAMember));
            return;
        }

        var membership = await db.HouseholdMembers
            .AsNoTracking()
            .Where(m => m.HouseholdId == householdId && m.UserId == userId)
            .Select(m => new { m.Role, m.IsOwner })
            .SingleOrDefaultAsync();

        if (membership is null)
        {
            context.Fail(new AuthorizationFailureReason(this, NotAMember));
            return;
        }

        var allowed = requirement.OwnerOnly
            ? membership.IsOwner
            : membership.Role.Satisfies(requirement.MinimumRole);

        if (allowed)
        {
            currentHousehold.Set(householdId);
            context.Succeed(requirement);
        }
    }

    private static bool TryGetHouseholdId(object? resource, out Guid householdId)
    {
        householdId = Guid.Empty;
        var routeValue = resource is HttpContext http ? http.GetRouteValue(HouseholdPolicies.RouteParameter) : null;
        return routeValue switch
        {
            Guid id => (householdId = id) != Guid.Empty,
            string text => Guid.TryParse(text, out householdId),
            _ => false,
        };
    }
}

/// <summary>
/// Turns "not a member" into 404 rather than 403, so the API doesn't reveal which households
/// exist. Members without a high enough role still get 403.
/// </summary>
public sealed class HouseholdAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        var notAMember = authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailureReasons.Any(r =>
                r.Handler is HouseholdMembershipHandler && r.Message == HouseholdMembershipHandler.NotAMember) == true;

        if (notAMember)
        {
            await Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Household not found").ExecuteAsync(context);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
