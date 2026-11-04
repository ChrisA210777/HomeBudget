using System.Security.Claims;
using Budget.Domain.Households;
using Budget.Infrastructure.Persistence;
using Budget.Server.Features.Households;
using Budget.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Budget.Server.Tests.Households;

/// <summary>Every role against every policy, straight through the authorisation handler.</summary>
[Collection(ApiCollection.Name)]
public class HouseholdMembershipHandlerTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    public static TheoryData<HouseholdRole, bool, HouseholdRole, bool, bool> Cases => new()
    {
        // member role, member is owner, required role, owner only, allowed
        { HouseholdRole.Viewer, false, HouseholdRole.Viewer, false, true },
        { HouseholdRole.Viewer, false, HouseholdRole.User, false, false },
        { HouseholdRole.Viewer, false, HouseholdRole.Admin, false, false },
        { HouseholdRole.User, false, HouseholdRole.Viewer, false, true },
        { HouseholdRole.User, false, HouseholdRole.User, false, true },
        { HouseholdRole.User, false, HouseholdRole.Admin, false, false },
        { HouseholdRole.Admin, false, HouseholdRole.Admin, false, true },
        { HouseholdRole.Admin, false, HouseholdRole.Admin, true, false },
        { HouseholdRole.Admin, true, HouseholdRole.Admin, true, true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Allows_members_with_a_high_enough_role(
        HouseholdRole memberRole, bool memberIsOwner, HouseholdRole requiredRole, bool ownerOnly, bool expectedAllowed)
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        var userId = memberIsOwner ? ownerId : await AddMemberAsync(householdId, memberRole);

        var (succeeded, notAMember, currentHousehold) =
            await AuthorizeAsync(userId, householdId, new HouseholdRequirement(requiredRole, ownerOnly));

        Assert.Equal(expectedAllowed, succeeded);
        Assert.False(notAMember);
        Assert.Equal(expectedAllowed ? (Guid?)householdId : null, currentHousehold);
    }

    [Fact]
    public async Task Non_members_fail_as_not_a_member_and_no_household_is_set()
    {
        var (_, householdId) = await CreateHouseholdOwnerAsync();
        var outsider = await CreateUserAsync();

        var (succeeded, notAMember, currentHousehold) =
            await AuthorizeAsync(outsider.Id, householdId, new HouseholdRequirement(HouseholdRole.Viewer));

        Assert.False(succeeded);
        Assert.True(notAMember);
        Assert.Null(currentHousehold);
    }

    private async Task<(bool Succeeded, bool NotAMember, Guid? CurrentHousehold)> AuthorizeAsync(
        Guid userId, Guid householdId, HouseholdRequirement requirement)
    {
        var result = (false, false, (Guid?)null);

        await ExecuteScopeAsync(async services =>
        {
            var http = new DefaultHttpContext { RequestServices = services };
            http.Request.RouteValues[HouseholdPolicies.RouteParameter] = householdId.ToString();
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));
            var context = new AuthorizationHandlerContext([requirement], user, http);

            var handler = services.GetServices<IAuthorizationHandler>().OfType<HouseholdMembershipHandler>().Single();
            await handler.HandleAsync(context);

            var notAMember = context.FailureReasons.Any(r => r.Message == HouseholdMembershipHandler.NotAMember);
            result = (context.HasSucceeded, notAMember, services.GetRequiredService<ICurrentHousehold>().HouseholdId);
        });

        return result;
    }
}
