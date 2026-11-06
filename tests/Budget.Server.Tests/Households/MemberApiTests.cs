using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared.Households;

namespace Budget.Server.Tests.Households;

/// <summary>GET /api/households/{id}/members (BUD-26).</summary>
[Collection(ApiCollection.Name)]
public class MemberApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Lists_the_owner_first_then_members_by_name_with_their_roles()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync("Zoe Owner");
        var viewerId = await AddMemberAsync(householdId, HouseholdRole.Viewer, "Bob Viewer");
        var userId = await AddMemberAsync(householdId, HouseholdRole.User, "Amy User");
        using var client = CreateClient(signedInAs: viewerId);

        var members = await client.GetFromJsonAsync<List<HouseholdMemberSummary>>(new Uri(MemberRoutes.Members(householdId), UriKind.Relative));

        Assert.NotNull(members);
        Assert.Equal(
            [(ownerId, "Zoe Owner", "Admin", true), (userId, "Amy User", "User", false), (viewerId, "Bob Viewer", "Viewer", false)],
            members.Select(m => (m.UserId, m.DisplayName, m.Role, m.IsOwner)));
        Assert.All(members, m => Assert.True(m.JoinedAt > DateTimeOffset.UtcNow.AddMinutes(-5)));
        Assert.Contains("@example.com", members[1].Email, StringComparison.Ordinal);
    }

    [Fact]
    public Task Other_households_cannot_list_them() =>
        AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, MemberRoutes.Members);

    [Fact]
    public Task Every_role_can_list_them() =>
        AssertMinimumRoleAsync(HttpMethod.Get, MemberRoutes.Members, HouseholdRole.Viewer);
}
