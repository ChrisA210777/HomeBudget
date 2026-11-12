using Budget.Client.Account;
using Budget.Client.Activity;
using Budget.Client.Pages.Activity;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Activity;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Activity;

/// <summary>The Recent activity page (BUD-140).</summary>
public sealed class ActivityPageTests : IDisposable
{
    private static readonly Guid ChrisUser = Guid.NewGuid();
    private readonly TestContext _context = new();
    private readonly FakeActivityApi _api = new();

    public ActivityPageTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IActivityApi>(_api);
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi("Viewer", isOwner: false));
        _context.Services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero)));
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Changes_are_grouped_by_day_newest_first_with_the_time()
    {
        var cut = _context.RenderComponent<ActivityPage>();

        Assert.Equal(new[] { "Today", "Yesterday", "Monday 9 November" }, Texts(cut, "activity-day-name"));
        Assert.Equal(
            new[] { "Chris paused Netflix from 1 Dec 2026", "Sam added a new yearly bill: Car insurance, £480.00", "Chris renamed the household from Home to The Austins" },
            Texts(cut, "activity-text"));
        Assert.Equal(new[] { "09:30", "18:05", "08:00" }, Texts(cut, "activity-when"));
        Assert.Equal((30, (Guid?)null, 0), _api.Requests.Single());
    }

    [Fact]
    public void Filtering_by_who_and_type_asks_for_just_those()
    {
        var popovers = _context.RenderComponent<MudPopoverProvider>();
        var cut = _context.RenderComponent<ActivityPage>();

        Choose(cut, popovers, "filter-who", "Chris");
        cut.WaitForAssertion(() => Assert.Equal((30, (Guid?)ChrisUser, 0), _api.Requests[^1]));
        Choose(cut, popovers, "filter-type", "Outgoings");
        cut.WaitForAssertion(() => Assert.Equal(new[] { ActivityAreaDto.Outgoings, ActivityAreaDto.Categories }, _api.Areas[^1]));
    }

    [Fact]
    public void Show_more_adds_older_changes()
    {
        _api.HasMore = true;
        var cut = _context.RenderComponent<ActivityPage>();

        cut.Find("[data-testid=show-more]").Click();

        cut.WaitForAssertion(() => Assert.Equal(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero), _api.Befores[^1]));
    }

    [Fact]
    public void Says_so_when_nothing_has_changed()
    {
        _api.Items = [];
        var cut = _context.RenderComponent<ActivityPage>();

        Assert.Equal("No recent activity", Text(cut, "activity-empty"));
    }

    private static void Choose(IRenderedComponent<ActivityPage> cut, IRenderedComponent<MudPopoverProvider> popovers, string filter, string option)
    {
        cut.Find($"[data-testid={filter}] button").Click();
        popovers.WaitForElements($"[data-testid={filter}-option]").Single(o => o.TextContent.Trim() == option).Click();
        popovers.WaitForAssertion(() => Assert.Empty(popovers.FindAll($"[data-testid={filter}-option]")));
    }

    private static string Text(IRenderedComponent<ActivityPage> cut, string testId) => cut.Find($"[data-testid={testId}]").TextContent.Trim();

    private static string[] Texts(IRenderedComponent<ActivityPage> cut, string testId) =>
        cut.FindAll($"[data-testid={testId}]").Select(e => e.TextContent.Trim()).ToArray();

    private sealed class FakeActivityApi : IActivityApi
    {
        public IReadOnlyList<ActivityItem> Items { get; set; } =
        [
            new(Guid.NewGuid(), new DateTimeOffset(2026, 11, 12, 9, 30, 0, TimeSpan.Zero), "Chris", "paused Netflix from 1 Dec 2026", ActivityAreaDto.Outgoings, Guid.NewGuid(), Exists: true),
            new(Guid.NewGuid(), new DateTimeOffset(2026, 11, 11, 18, 5, 0, TimeSpan.Zero), "Sam", "added a new yearly bill: Car insurance, {{480.00}}", ActivityAreaDto.Outgoings, Guid.NewGuid(), Exists: true),
            new(Guid.NewGuid(), new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero), "Chris", "renamed the household from Home to The Austins", ActivityAreaDto.Household, null, Exists: false),
        ];

        public bool HasMore { get; set; }

        public List<(int Take, Guid? Who, int Areas)> Requests { get; } = [];

        public List<ActivityAreaDto[]> Areas { get; } = [];

        public List<DateTimeOffset?> Befores { get; } = [];

        public Task<ActivityResponse> GetAsync(
            Guid householdId, int take, int? days = null, DateTimeOffset? before = null, Guid? who = null, IReadOnlyCollection<ActivityAreaDto>? areas = null)
        {
            Requests.Add((take, who, areas?.Count ?? 0));
            Areas.Add([.. areas ?? []]);
            Befores.Add(before);
            return Task.FromResult(new ActivityResponse("GBP", before is null ? Items : [])
            {
                People = [new(ChrisUser, "Chris"), new(Guid.NewGuid(), "Sam")],
                HasMore = HasMore && before is null,
            });
        }
    }
}
