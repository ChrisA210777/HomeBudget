using Budget.Client.Account;
using Budget.Client.Households;
using Budget.Client.Pages.Households;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Households;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Households;

public sealed class MembersTests : IDisposable
{
    private readonly TestContext _context = new();
    private readonly StubHouseholdApi _api = new();

    public MembersTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IHouseholdApi>(_api);
        _context.Services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero)));
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi("Viewer", isOwner: false));
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void Shows_each_members_name_email_role_and_when_they_joined()
    {
        var cut = _context.RenderComponent<Members>();

        var members = cut.FindAll("[data-testid=member]").ToList();
        Assert.Equal(2, members.Count);

        Assert.Equal("Jane Austin", Text(members[0], "member-name"));
        Assert.Equal("jane@example.com", Text(members[0], "member-email"));
        Assert.Equal("Admin", Text(members[0], "member-role"));
        Assert.Equal("Owner", Text(members[0], "member-owner"));
        Assert.Equal("Joined 4 Nov 2026", Text(members[0], "member-joined"));
        Assert.Null(members[0].QuerySelector("[data-testid=member-you]"));

        Assert.Equal("Chris Austin", Text(members[1], "member-name"));
        Assert.Equal("Viewer", Text(members[1], "member-role"));
        Assert.Null(members[1].QuerySelector("[data-testid=member-owner]"));
        Assert.NotNull(members[1].QuerySelector("[data-testid=member-you]"));
        Assert.Null(members[1].QuerySelector("[data-testid=member-email]"));
    }

    [Fact]
    public void Lists_the_active_households_members_for_any_role()
    {
        var cut = _context.RenderComponent<Members>();

        cut.WaitForAssertion(() => Assert.Equal([FakeAccountApi.HouseholdId], _api.Requested));
        Assert.Contains("My Household", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid=role-help]"));
    }

    public void Dispose() => _context.Dispose();

    private static string Text(AngleSharp.Dom.IElement member, string testId) =>
        member.QuerySelector($"[data-testid={testId}]")!.TextContent.Trim();

    private sealed class StubHouseholdApi : IHouseholdApi
    {
        public List<Guid> Requested { get; } = [];

        public Task<RenameHouseholdResult> RenameAsync(Guid householdId, RenameHouseholdRequest request) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<HouseholdMemberSummary>> GetMembersAsync(Guid householdId)
        {
            Requested.Add(householdId);
            return Task.FromResult<IReadOnlyList<HouseholdMemberSummary>>(
            [
                new(Guid.NewGuid(), "Jane Austin", "jane@example.com", "Admin", true, new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero)),
                new(FakeAccountApi.UserId, "Chris Austin", null, "Viewer", false, new DateTimeOffset(2026, 11, 5, 23, 30, 0, TimeSpan.FromHours(-1))),
            ]);
        }
    }
}
