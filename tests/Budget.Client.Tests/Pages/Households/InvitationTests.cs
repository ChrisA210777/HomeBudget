using Budget.Client.Account;
using Budget.Client.Households;
using Budget.Client.Pages.Households;
using Budget.Client.People;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Households;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Households;

/// <summary>Inviting people and accepting invitations (ADR-015).</summary>
public sealed class InvitationTests : IDisposable
{
    private readonly TestContext _context = new();
    private readonly FakeInvitationApi _api = new();
    private readonly FakeSharing _sharing = new();

    public InvitationTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IInvitationApi>(_api);
        _context.Services.AddSingleton<IPeopleApi>(new StaticPeopleApi());
        _context.Services.AddSingleton<ISharing>(_sharing);
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi());
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public async Task An_admin_invites_someone_as_a_person_and_shares_the_link()
    {
        var cut = Panel();
        Assert.NotNull(cut.Find("[data-testid=no-invitations]"));

        cut.Find("[data-testid=invite]").Click();
        // Only people not yet signing in are offered, plus someone new.
        cut.Find("input[data-testid=invite-email]").Input("jane@example.com");
        await cut.InvokeAsync(() => cut.FindComponents<MudSelect<Guid?>>()[0].Instance.ValueChanged.InvokeAsync(StaticPeopleApi.JaneId));
        cut.Find("[data-testid=invite-create]").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=invitation-ready]")));
        var request = Assert.Single(_api.Created);
        Assert.Equal(("jane@example.com", "User", (Guid?)StaticPeopleApi.JaneId, false), (request.Email, request.Role, request.PersonId, request.RequireEmailMatch));
        Assert.Equal("User · Jane · Expires 18 Nov", cut.Find("[data-testid=invitation-details]").TextContent.Trim());

        cut.Find("[data-testid=share-invitation]").Click();
        cut.WaitForAssertion(() => Assert.Equal(("Join My Household on Home Budget", "https://budget.test/invite/abc123"), (_sharing.Shared.Single().Title, _sharing.Shared.Single().Url)));

        cut.Find("[data-testid=copy-invitation]").Click();
        cut.WaitForAssertion(() => Assert.Equal(new[] { "https://budget.test/invite/abc123" }, _sharing.Copied));

        var mailto = cut.Find("[data-testid=email-invitation]").GetAttribute("href")!;
        Assert.StartsWith("mailto:jane%40example.com?subject=Join%20My%20Household%20on%20Home%20Budget&body=", mailto, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("https://budget.test/invite/abc123"), mailto, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bad_email_is_shown_and_nothing_is_created()
    {
        var cut = Panel();

        cut.Find("[data-testid=invite]").Click();
        cut.Find("[data-testid=invite-create]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Enter a valid email address.", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(_api.Pending);
    }

    [Fact]
    public void Pending_invitations_can_be_revoked()
    {
        var id = Guid.NewGuid();
        _api.Pending.Add(new InvitationSummary(id, "sam@example.com", "Viewer", null, null, true, "Chris Austin", new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero), "https://budget.test/invite/x"));
        var cut = Panel();

        Assert.Equal("Viewer · Someone new · Email must match · Expires 15 Nov", cut.Find("[data-testid=invitation-details]").TextContent.Trim());
        cut.Find("[data-testid=revoke-invitation]").Click();

        cut.WaitForAssertion(() => Assert.Equal(new[] { id }, _api.Revoked));
        Assert.NotNull(cut.Find("[data-testid=no-invitations]"));
    }

    [Fact]
    public void Without_a_share_sheet_there_is_still_copy_and_email()
    {
        _context.Services.AddSingleton<ISharing>(new FakeSharing(canShare: false));
        _api.Pending.Add(new InvitationSummary(Guid.NewGuid(), "sam@example.com", "User", null, null, false, "Chris Austin", DateTimeOffset.UtcNow.AddDays(7), "https://budget.test/invite/x"));

        var cut = Panel();

        Assert.Empty(cut.FindAll("[data-testid=share-invitation]"));
        Assert.NotNull(cut.Find("[data-testid=copy-invitation]"));
        Assert.NotNull(cut.Find("[data-testid=email-invitation]"));
    }

    [Fact]
    public void The_invitation_page_says_who_invited_you_and_joins_on_accepting()
    {
        _api.Preview = Preview();
        _api.AcceptResult = (new AcceptInvitationResponse(Guid.NewGuid(), "The Austins"), null);

        var cut = _context.RenderComponent<InvitePage>(p => p.Add(c => c.Token, "abc123"));

        Assert.Equal("Chris invited you to The Austins as a User, as Jane.", Normalise(cut.Find("[data-testid=invite-summary]").TextContent));
        cut.Find("[data-testid=accept-invite]").Click();

        cut.WaitForAssertion(() => Assert.Equal(new[] { "abc123" }, _api.Accepted));
        Assert.Equal("http://localhost/", _context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void An_invitation_for_another_email_explains_which_account_to_use()
    {
        _api.Preview = Preview() with { RequireEmailMatch = true, EmailAllowed = false, InvitedEmail = "jane@example.com", SignedInEmail = "chris@example.com" };

        var cut = _context.RenderComponent<InvitePage>(p => p.Add(c => c.Token, "abc123"));

        Assert.Contains("This invitation is for jane@example.com, but you're signed in as chris@example.com.", Normalise(cut.Find("[data-testid=invite-wrong-email]").TextContent), StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid=accept-invite]"));
        Assert.NotNull(cut.Find("[data-testid=invite-switch-account]"));
    }

    [Theory]
    [InlineData("Expired", "invite-expired")]
    [InlineData("Revoked", "invite-revoked")]
    [InlineData("Accepted", "invite-used")]
    public void Used_revoked_and_expired_links_say_so(string status, string testId)
    {
        _api.Preview = Preview() with { Status = status };

        var cut = _context.RenderComponent<InvitePage>(p => p.Add(c => c.Token, "abc123"));

        Assert.NotNull(cut.Find($"[data-testid={testId}]"));
        Assert.Empty(cut.FindAll("[data-testid=accept-invite]"));
    }

    [Fact]
    public void An_unknown_link_says_so()
    {
        var cut = _context.RenderComponent<InvitePage>(p => p.Add(c => c.Token, "nope"));

        Assert.NotNull(cut.Find("[data-testid=invite-not-found]"));
    }

    public void Dispose() => _context.Dispose();

    private IRenderedComponent<InvitationsPanel> Panel() =>
        _context.RenderComponent<InvitationsPanel>(p => p.Add(c => c.HouseholdId, FakeAccountApi.HouseholdId).Add(c => c.HouseholdName, "My Household"));

    private static InvitationPreview Preview() => new(
        "The Austins", "Chris", "User", "Jane", new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero), "Pending", false, true, null, "jane@example.com", false);

    private static string Normalise(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
