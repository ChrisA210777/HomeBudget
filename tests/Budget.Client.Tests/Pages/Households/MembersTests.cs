using Budget.Client.Account;
using Budget.Client.Households;
using Budget.Client.Pages.Households;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Households;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Households;

public sealed class MembersTests : IDisposable
{
    private static readonly Guid JaneId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BobId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly TestContext _context = new();
    private readonly StubHouseholdApi _api = new();

    public MembersTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IHouseholdApi>(_api);
        _context.Services.AddSingleton<Budget.Client.Households.IInvitationApi>(new FakeInvitationApi());
        _context.Services.AddSingleton<Budget.Client.People.IPeopleApi>(new StaticPeopleApi());
        _context.Services.AddSingleton<Budget.Client.Households.ISharing>(new FakeSharing());
        _context.Services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero)));
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void Shows_each_members_name_email_role_and_when_they_joined()
    {
        var cut = Render("Viewer", isOwner: false);

        var members = cut.FindAll("[data-testid=member]").ToList();
        Assert.Equal(3, members.Count);

        Assert.Equal("Jane Austin", Text(members[0], "member-name"));
        Assert.Equal("jane@example.com", Text(members[0], "member-email"));
        Assert.Equal("Admin", Text(members[0], "member-role"));
        Assert.Equal("Owner", Text(members[0], "member-owner"));
        Assert.Equal("Joined 4 Nov 2026", Text(members[0], "member-joined"));
        Assert.Null(members[0].QuerySelector("[data-testid=member-you]"));

        Assert.Equal("Chris Austin", Text(members[2], "member-name"));
        Assert.Equal("Viewer", Text(members[2], "member-role"));
        Assert.Null(members[2].QuerySelector("[data-testid=member-owner]"));
        Assert.NotNull(members[2].QuerySelector("[data-testid=member-you]"));
        Assert.Null(members[2].QuerySelector("[data-testid=member-email]"));
    }

    [Fact]
    public void Lists_the_active_households_members_for_any_role()
    {
        var cut = Render("Viewer", isOwner: false);

        cut.WaitForAssertion(() => Assert.Equal([FakeAccountApi.HouseholdId], _api.Requested));
        Assert.Contains("My Household", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid=role-help]"));
    }

    [Fact]
    public void Users_and_viewers_cant_change_roles_or_remove_anyone()
    {
        var cut = Render("User", isOwner: false);

        Assert.Empty(cut.FindAll("[data-testid=role-select]"));
        Assert.Empty(cut.FindAll("[data-testid=remove-member]"));
    }

    [Fact]
    public void Admins_can_manage_everyone_except_the_owner()
    {
        var cut = Render("Admin", isOwner: true);

        var members = cut.FindAll("[data-testid=member]").ToList();
        Assert.Null(members[0].QuerySelector("[data-testid=role-select]"));
        Assert.Null(members[0].QuerySelector("[data-testid=remove-member]"));
        Assert.NotNull(members[1].QuerySelector("[data-testid=remove-member]"));
        Assert.Equal(2, cut.FindComponents<MudSelect<string>>().Count);
    }

    [Fact]
    public async Task Changing_a_role_saves_it_and_reloads_the_list()
    {
        var cut = Render("Admin", isOwner: true);

        await cut.InvokeAsync(() => cut.FindComponents<MudSelect<string>>()[0].Instance.ValueChanged.InvokeAsync("Admin"));

        Assert.Equal([(BobId, "Admin")], _api.RoleChanges);
        cut.WaitForAssertion(() => Assert.Equal(2, _api.Requested.Count));
    }

    [Fact]
    public async Task A_refused_change_is_explained()
    {
        _api.Refusal = "The household needs at least one Admin. Make someone else an Admin first.";
        var cut = Render("Admin", isOwner: true);

        await cut.InvokeAsync(() => cut.FindComponents<MudSelect<string>>()[0].Instance.ValueChanged.InvokeAsync("Viewer"));

        cut.WaitForAssertion(() => Assert.Contains("at least one Admin", cut.Find("[data-testid=member-error]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Removing_someone_asks_first_then_removes_them()
    {
        var cut = Render("Admin", isOwner: true);

        cut.FindAll("[data-testid=remove-member]").First().Click();
        Assert.Contains("Remove Bob Austin from this household?", cut.Find("[data-testid=remove-confirm]").TextContent, StringComparison.Ordinal);
        Assert.Empty(_api.Removed);

        cut.Find("[data-testid=remove-yes]").Click();

        cut.WaitForAssertion(() => Assert.Equal([BobId], _api.Removed));
        Assert.Empty(cut.FindAll("[data-testid=remove-confirm]"));
    }

    [Fact]
    public void Cancelling_a_removal_removes_nobody()
    {
        var cut = Render("Admin", isOwner: true);

        cut.FindAll("[data-testid=remove-member]").First().Click();
        cut.Find("[data-testid=remove-cancel]").Click();

        Assert.Empty(_api.Removed);
        Assert.Empty(cut.FindAll("[data-testid=remove-confirm]"));
    }

    [Fact]
    public void The_owner_is_told_they_cant_leave()
    {
        var cut = Render("Admin", isOwner: true);

        Assert.Contains("delete the household", cut.Find("[data-testid=owner-cant-leave]").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid=leave]"));
    }

    [Fact]
    public void Leaving_asks_first_then_reloads_the_app_at_home()
    {
        var cut = Render("Viewer", isOwner: false);
        var navigation = _context.Services.GetRequiredService<NavigationManager>();

        cut.Find("[data-testid=leave]").Click();
        Assert.False(_api.Left);
        cut.Find("[data-testid=leave-yes]").Click();

        cut.WaitForAssertion(() => Assert.True(_api.Left));
        Assert.Equal("http://localhost/", navigation.Uri);
    }

    [Fact]
    public void Only_the_owner_can_delete_the_household()
    {
        var cut = Render("Admin", isOwner: false);

        Assert.Empty(cut.FindAll("[data-testid=delete-section]"));
    }

    [Fact]
    public void Deleting_needs_the_household_name_typed_then_reloads_the_app_at_home()
    {
        var cut = Render("Admin", isOwner: true);
        var navigation = _context.Services.GetRequiredService<NavigationManager>();

        cut.Find("[data-testid=delete-household]").Click();
        Assert.True(cut.Find("[data-testid=delete-yes]").HasAttribute("disabled"));

        cut.Find("input[data-testid=delete-name]").Input("my household ");
        cut.Find("[data-testid=delete-yes]").Click();

        cut.WaitForAssertion(() => Assert.Equal([FakeAccountApi.HouseholdId], _api.Deleted));
        Assert.Equal("http://localhost/", navigation.Uri);
    }

    [Fact]
    public void Cancelling_a_delete_deletes_nothing()
    {
        var cut = Render("Admin", isOwner: true);

        cut.Find("[data-testid=delete-household]").Click();
        cut.Find("input[data-testid=delete-name]").Input("My Household");
        cut.Find("[data-testid=delete-cancel]").Click();

        Assert.Empty(_api.Deleted);
        Assert.NotNull(cut.Find("[data-testid=delete-household]"));
    }

    public void Dispose() => _context.Dispose();

    private IRenderedComponent<Members> Render(string role, bool isOwner)
    {
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role, isOwner));
        _context.Services.AddScoped<UserSession>();
        _api.YourRole = role;
        _api.YouOwn = isOwner;
        return _context.RenderComponent<Members>();
    }

    private static string Text(AngleSharp.Dom.IElement member, string testId) =>
        member.QuerySelector($"[data-testid={testId}]")!.TextContent.Trim();

    private sealed class StubHouseholdApi : IHouseholdApi
    {
        public List<Guid> Requested { get; } = [];

        public List<(Guid UserId, string Role)> RoleChanges { get; } = [];

        public List<Guid> Removed { get; } = [];

        public bool Left { get; private set; }

        public string? Refusal { get; set; }

        public string YourRole { get; set; } = "Viewer";

        public bool YouOwn { get; set; }

        public Task<CreateHouseholdResult> CreateAsync(CreateHouseholdRequest request) => throw new NotSupportedException();

        public Task<HouseholdSetupResponse> GetSetupAsync(Guid householdId) => throw new NotSupportedException();

        public Task<bool> SwitchToAsync(Guid householdId) => throw new NotSupportedException();

        public Task<HouseholdResponse> SaveSettingsAsync(Guid householdId, HouseholdSettingsRequest request) => throw new NotSupportedException();

        public Task<RenameHouseholdResult> RenameAsync(Guid householdId, RenameHouseholdRequest request) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<HouseholdMemberSummary>> GetMembersAsync(Guid householdId)
        {
            Requested.Add(householdId);
            IReadOnlyList<HouseholdMemberSummary> members = YouOwn
                ?
                [
                    new(FakeAccountApi.UserId, "Chris Austin", null, "Admin", true, new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero)),
                    new(BobId, "Bob Austin", "bob@example.com", "User", false, new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero)),
                    new(JaneId, "Jane Austin", "jane@example.com", "Viewer", false, new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero)),
                ]
                :
                [
                    new(JaneId, "Jane Austin", "jane@example.com", "Admin", true, new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero)),
                    new(BobId, "Bob Austin", "bob@example.com", "User", false, new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero)),
                    new(FakeAccountApi.UserId, "Chris Austin", null, YourRole, false, new DateTimeOffset(2026, 11, 5, 23, 30, 0, TimeSpan.FromHours(-1))),
                ];
            return Task.FromResult(members);
        }

        public Task<string?> ChangeRoleAsync(Guid householdId, Guid userId, string role)
        {
            RoleChanges.Add((userId, role));
            return Task.FromResult(Refusal);
        }

        public Task<string?> RemoveMemberAsync(Guid householdId, Guid userId)
        {
            Removed.Add(userId);
            return Task.FromResult(Refusal);
        }

        public List<Guid> Deleted { get; } = [];

        public Task DeleteAsync(Guid householdId)
        {
            Deleted.Add(householdId);
            return Task.CompletedTask;
        }

        public Task<string?> LeaveAsync(Guid householdId)
        {
            Left = true;
            return Task.FromResult(Refusal);
        }
    }
}
