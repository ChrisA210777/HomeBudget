using Budget.Client.Account;
using Budget.Client.People;
using Budget.Client.Pages.People;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.People;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.People;

public sealed class PeopleListTests : IDisposable
{
    private readonly TestContext _context = new();
    private readonly FakePeopleApi _peopleApi = new();

    public PeopleListTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IPeopleApi>(_peopleApi);
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
        UseRole("Admin");
    }

    [Fact]
    public void Shows_each_person_and_whether_they_sign_in()
    {
        var cut = _context.RenderComponent<PeopleList>();

        var people = cut.FindAll("[data-testid=person]").ToList();
        Assert.Equal(2, people.Count);
        Assert.Equal("Chris Austin", people[0].QuerySelector("[data-testid=person-name]")!.TextContent.Trim());
        Assert.Equal("Signs in as Chris Austin", people[0].QuerySelector("[data-testid=person-login]")!.TextContent.Trim());
        Assert.Contains("You", people[0].TextContent, StringComparison.Ordinal);
        Assert.Equal("Jane", people[1].QuerySelector("[data-testid=person-name]")!.TextContent.Trim());
        Assert.Equal("Doesn't sign in", people[1].QuerySelector("[data-testid=person-login]")!.TextContent.Trim());
    }

    [Fact]
    public void Adding_someone_sends_their_name_and_reloads()
    {
        var cut = _context.RenderComponent<PeopleList>();

        cut.Find("input[data-testid=add-name]").Change("Alex");
        cut.Find("[data-testid=add-person]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { "Alex" }, _peopleApi.Added);
            Assert.Equal(2, _peopleApi.ListCalls);
        });
    }

    [Fact]
    public void A_blank_name_is_rejected_before_anything_is_sent()
    {
        var cut = _context.RenderComponent<PeopleList>();

        cut.Find("[data-testid=add-person]").Click();

        Assert.Empty(_peopleApi.Added);
        Assert.Contains("Enter a name.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duplicate_name_from_the_server_is_shown()
    {
        _peopleApi.NextError = "Someone in this household already has that name.";
        var cut = _context.RenderComponent<PeopleList>();

        cut.Find("input[data-testid=add-name]").Change("jane");
        cut.Find("[data-testid=add-person]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Someone in this household already has that name.", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Viewers_cannot_add_or_rename()
    {
        UseRole("Viewer", isOwner: false);

        var cut = _context.RenderComponent<PeopleList>();

        Assert.Empty(cut.FindAll("[data-testid=add-person]"));
        Assert.Empty(cut.FindAll("[data-testid=rename]"));
        Assert.Equal(2, cut.FindAll("[data-testid=person]").Count);
    }

    public void Dispose() => _context.Dispose();

    private void UseRole(string role, bool isOwner = true)
    {
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role, isOwner));
        _context.Services.AddScoped<UserSession>();
    }

    private sealed class FakePeopleApi : IPeopleApi
    {
        public List<string> Added { get; } = [];

        public int ListCalls { get; private set; }

        public string? NextError { get; set; }

        public Task<IReadOnlyList<PersonSummary>> ListAsync(Guid householdId)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<PersonSummary>>(
            [
                new(Guid.NewGuid(), "Chris Austin", FakeAccountApi.UserId, "Chris Austin", IsYou: true),
                new(Guid.NewGuid(), "Jane", null, null, IsYou: false),
            ]);
        }

        public Task<SavePersonResult> AddAsync(Guid householdId, SavePersonRequest request)
        {
            if (NextError is { } error)
            {
                return Task.FromResult(new SavePersonResult(null, new Dictionary<string, string[]> { ["DisplayName"] = [error] }));
            }

            Added.Add(request.DisplayName);
            return Task.FromResult(new SavePersonResult(new PersonSummary(Guid.NewGuid(), request.DisplayName, null, null, false), new Dictionary<string, string[]>()));
        }

        public Task<SavePersonResult> RenameAsync(Guid householdId, Guid personId, SavePersonRequest request) =>
            Task.FromResult(new SavePersonResult(new PersonSummary(personId, request.DisplayName, null, null, false), new Dictionary<string, string[]>()));
    }
}
