using System.Net;
using System.Net.Http.Json;
using Budget.Domain.Households;
using Budget.Domain.People;
using Budget.Server.Tests.Infrastructure;
using Budget.Shared;
using Budget.Shared.Account;
using Budget.Shared.Households;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Budget.Server.Tests.Households;

/// <summary>Invitations to join a household (ADR-015, BUD-23, BUD-24, BUD-25, BUD-44).</summary>
[Collection(ApiCollection.Name)]
public class InvitationApiTests(BudgetApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task An_invitation_links_the_new_member_to_their_person_and_drops_their_empty_starter_household()
    {
        var (owner, householdId) = await OwnerAsync();
        var jane = await PersonAsync(householdId, "Jane");
        var invitation = await InviteAsync(owner, householdId, new CreateInvitationRequest { Email = "jane@example.com", Role = "User", PersonId = jane });
        Assert.Equal(("jane@example.com", "Jane", "User"), (invitation.Email, invitation.PersonName, invitation.Role));
        Assert.Contains("/invite/", invitation.Link, StringComparison.Ordinal);

        var (inviteeId, starterHousehold) = await CreateHouseholdOwnerAsync("Jane Austin");
        using var invitee = CreateClient(signedInAs: inviteeId);
        var token = Token(invitation);

        var preview = (await invitee.GetFromJsonAsync<InvitationPreview>(Url(InvitationRoutes.ByToken(token))))!;
        Assert.Equal(("My Household", "Household Owner", "User", "Jane", "Pending", true, false), (preview.HouseholdName, preview.InvitedByName, preview.Role, preview.PersonName, preview.Status, preview.EmailAllowed, preview.AlreadyMember));

        using var accepted = await invitee.PostAsync(Url(InvitationRoutes.Accept(token)), null);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        var me = (await invitee.GetFromJsonAsync<CurrentUserResponse>(Url(AccountRoutes.CurrentUser)))!;
        var joined = Assert.Single(me.Households);
        Assert.Equal((householdId, "User", (Guid?)jane, householdId), (joined.HouseholdId, joined.Role, joined.PersonId, me.LastActiveHouseholdId));
        await ExecuteDbContextAsync(async db => Assert.False(await db.Households.AnyAsync(h => h.Id == starterHousehold)));

        // Single use, and the email is forgotten.
        using var again = await invitee.PostAsync(Url(InvitationRoutes.Accept(token)), null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        await ExecuteDbContextAsync(async db => Assert.Null((await db.Invitations.SingleAsync(i => i.Id == invitation.Id)).Email));
        Assert.Empty((await owner.GetFromJsonAsync<List<InvitationSummary>>(Url(InvitationRoutes.Invitations(householdId))))!);
    }

    [Fact]
    public async Task Someone_new_gets_a_person_of_their_own_and_a_household_with_data_is_kept()
    {
        var (owner, householdId) = await OwnerAsync();
        var invitation = await InviteAsync(owner, householdId, new CreateInvitationRequest { Email = "sam@example.com", Role = "Viewer" });
        var sam = await CreateUserAsync("Sam Smith");
        using var client = CreateClient(signedInAs: sam.Id);

        using var accepted = await client.PostAsync(Url(InvitationRoutes.Accept(Token(invitation))), null);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await ExecuteDbContextAsync(async db =>
        {
            var person = await db.People.IgnoreQueryFilters().SingleAsync(p => p.HouseholdId == householdId && p.LinkedUserId == sam.Id);
            Assert.Equal("Sam", person.DisplayName);
            Assert.Equal(HouseholdRole.Viewer, (await db.HouseholdMembers.SingleAsync(m => m.HouseholdId == householdId && m.UserId == sam.Id)).Role);
        });
    }

    [Fact]
    public async Task When_asked_only_the_matching_email_can_accept()
    {
        var (owner, householdId) = await OwnerAsync();
        var invitation = await InviteAsync(owner, householdId, new CreateInvitationRequest { Email = "Jane@Example.com", RequireEmailMatch = true });
        var other = await CreateUserAsync("Someone Else", "other@example.com");
        var jane = await CreateUserAsync("Jane", "jane@example.com");
        var token = Token(invitation);

        using var otherClient = CreateClient(signedInAs: other.Id);
        var preview = (await otherClient.GetFromJsonAsync<InvitationPreview>(Url(InvitationRoutes.ByToken(token))))!;
        Assert.Equal((false, "Jane@Example.com", "other@example.com"), (preview.EmailAllowed, preview.InvitedEmail, preview.SignedInEmail));
        using var refused = await otherClient.PostAsync(Url(InvitationRoutes.Accept(token)), null);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using var janeClient = CreateClient(signedInAs: jane.Id);
        using var accepted = await janeClient.PostAsync(Url(InvitationRoutes.Accept(token)), null);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task Revoked_and_unknown_links_cant_be_used()
    {
        var (owner, householdId) = await OwnerAsync();
        var invitation = await InviteAsync(owner, householdId, new CreateInvitationRequest { Email = "jane@example.com" });

        using var revoked = await owner.DeleteAsync(Url(InvitationRoutes.Invitation(householdId, invitation.Id)));
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        using var client = CreateClient(signedInAs: (await CreateUserAsync()).Id);
        Assert.Equal("Revoked", (await client.GetFromJsonAsync<InvitationPreview>(Url(InvitationRoutes.ByToken(Token(invitation)))))!.Status);
        using var refused = await client.PostAsync(Url(InvitationRoutes.Accept(Token(invitation))), null);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var unknown = await client.GetAsync(Url(InvitationRoutes.ByToken("not-a-real-token")));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Bad_invitations_are_refused()
    {
        var (owner, householdId) = await OwnerAsync();
        var jane = await PersonAsync(householdId, "Jane");
        await InviteAsync(owner, householdId, new CreateInvitationRequest { Email = "jane@example.com", PersonId = jane });

        foreach (var (request, field) in new[]
        {
            (new CreateInvitationRequest { Email = "not an email" }, nameof(CreateInvitationRequest.Email)),
            (new CreateInvitationRequest { Email = "a@example.com", Role = "Owner" }, nameof(CreateInvitationRequest.Role)),
            (new CreateInvitationRequest { Email = "a@example.com", PersonId = jane }, nameof(CreateInvitationRequest.PersonId)),
            (new CreateInvitationRequest { Email = "a@example.com", PersonId = Guid.NewGuid() }, nameof(CreateInvitationRequest.PersonId)),
        })
        {
            using var response = await owner.PostAsJsonAsync(Url(InvitationRoutes.Invitations(householdId)), request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(field, (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
        }
    }

    [Fact]
    public async Task Only_admins_invite_and_see_invitations()
    {
        await AssertMinimumRoleAsync(HttpMethod.Get, InvitationRoutes.Invitations, HouseholdRole.Admin);
        await AssertMinimumRoleAsync(HttpMethod.Post, InvitationRoutes.Invitations, HouseholdRole.Admin, () => new CreateInvitationRequest { Email = $"{Guid.NewGuid():N}@example.com" });
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Get, InvitationRoutes.Invitations);
        await AssertOtherHouseholdsCannotAccessAsync(HttpMethod.Post, InvitationRoutes.Invitations, new CreateInvitationRequest { Email = "a@example.com" });
    }

    private async Task<(HttpClient Client, Guid HouseholdId)> OwnerAsync()
    {
        var (ownerId, householdId) = await CreateHouseholdOwnerAsync();
        return (CreateClient(signedInAs: ownerId), householdId);
    }

    private async Task<Guid> PersonAsync(Guid householdId, string name)
    {
        var person = Person.Create(householdId, name);
        await ExecuteDbContextAsync(async db =>
        {
            db.People.Add(person);
            await db.SaveChangesAsync();
        });
        return person.Id;
    }

    private static async Task<InvitationSummary> InviteAsync(HttpClient client, Guid householdId, CreateInvitationRequest request)
    {
        using var response = await client.PostAsJsonAsync(Url(InvitationRoutes.Invitations(householdId)), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InvitationSummary>())!;
    }

    private static string Token(InvitationSummary invitation) => Uri.UnescapeDataString(invitation.Link![(invitation.Link!.LastIndexOf('/') + 1)..]);

    private static Uri Url(string path) => new(path, UriKind.Relative);
}

public class InvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Links_are_single_use_and_expire_after_a_week()
    {
        var (invitation, token) = Invitation.Create(Guid.NewGuid(), " jane@example.com ", HouseholdRole.User, null, false, Guid.NewGuid(), "Chris", Now, t => "protected:" + t);

        Assert.Equal(("jane@example.com", Invitation.Hash(token), "protected:" + token), (invitation.Email, invitation.TokenHash, invitation.ProtectedToken));
        Assert.Equal(InvitationStatus.Pending, invitation.StatusAt(Now.AddDays(6)));
        Assert.Equal(InvitationStatus.Expired, invitation.StatusAt(Now.AddDays(7)));
        Assert.Throws<InvalidOperationException>(() => invitation.Accept(Guid.NewGuid(), Now.AddDays(8)));

        invitation.Accept(Guid.NewGuid(), Now.AddDays(1));
        Assert.Equal((InvitationStatus.Accepted, (string?)null), (invitation.StatusAt(Now.AddDays(1)), invitation.Email));
        Assert.Throws<InvalidOperationException>(() => invitation.Revoke(Now.AddDays(2)));
    }

    [Theory]
    [InlineData(false, "someone@else.com", true)]
    [InlineData(true, "someone@else.com", false)]
    [InlineData(true, "JANE@example.com", true)]
    [InlineData(true, null, false)]
    public void Email_matching_is_optional(bool require, string? signedInAs, bool allowed)
    {
        var (invitation, _) = Invitation.Create(Guid.NewGuid(), "jane@example.com", HouseholdRole.User, null, require, Guid.NewGuid(), "Chris", Now, t => t);

        Assert.Equal(allowed, invitation.AllowsEmail(signedInAs));
    }
}
