using Budget.Client.Account;
using Budget.Client.Outgoings;
using Budget.Client.Pages.Outgoings;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Outgoings;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Outgoings;

public sealed class CategoriesTests : IDisposable
{
    private static readonly Guid HousingId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid PetsId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid OldId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private readonly TestContext _context = new();
    private readonly FakeCategoryApi _api = new();

    public CategoriesTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<ICategoryApi>(_api);
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
        UseRole("Admin");
    }

    [Fact]
    public void Shows_each_category_with_its_usage_and_whether_it_is_hidden()
    {
        var cut = _context.RenderComponent<Categories>();

        var rows = cut.FindAll("[data-testid=category]").ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal("Housing", rows[0].QuerySelector("[data-testid=category-name]")!.TextContent.Trim());
        Assert.Equal("2 outgoings", rows[0].QuerySelector("[data-testid=category-usage]")!.TextContent.Trim());
        Assert.Equal("Nothing in it yet", rows[1].QuerySelector("[data-testid=category-usage]")!.TextContent.Trim());
        Assert.Null(rows[0].QuerySelector("[data-testid=hidden]"));
        Assert.NotNull(rows[2].QuerySelector("[data-testid=hidden]"));
    }

    [Fact]
    public void Only_unused_categories_can_be_deleted()
    {
        var cut = _context.RenderComponent<Categories>();

        var rows = cut.FindAll("[data-testid=category]").ToList();
        Assert.Null(rows[0].QuerySelector("[data-testid=delete]"));
        Assert.NotNull(rows[1].QuerySelector("[data-testid=delete]"));
    }

    [Fact]
    public void Adding_a_category_sends_the_trimmed_name_and_reloads()
    {
        var cut = _context.RenderComponent<Categories>();

        cut.Find("input[data-testid=add-name]").Change("  Hobbies ");
        cut.Find("[data-testid=add-category]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new[] { "Hobbies" }, _api.Added);
            Assert.Equal(2, _api.ListCalls);
        });
    }

    [Fact]
    public void A_blank_name_is_rejected_before_anything_is_sent()
    {
        var cut = _context.RenderComponent<Categories>();

        cut.Find("[data-testid=add-category]").Click();

        Assert.Empty(_api.Added);
        Assert.Contains("Enter a name for the category.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duplicate_name_from_the_server_is_shown()
    {
        _api.NextResult = new CategoryResult(false, new Dictionary<string, string[]> { ["Name"] = ["There's already a category with that name."] });
        var cut = _context.RenderComponent<Categories>();

        cut.Find("input[data-testid=add-name]").Change("housing");
        cut.Find("[data-testid=add-category]").Click();

        cut.WaitForAssertion(() => Assert.Contains("already a category with that name", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Hiding_keeps_the_name_and_flips_the_flag()
    {
        var cut = _context.RenderComponent<Categories>();

        cut.FindAll("[data-testid=toggle-hidden]").ToList()[0].Click();
        cut.FindAll("[data-testid=toggle-hidden]").ToList()[2].Click();

        cut.WaitForAssertion(() => Assert.Equal(
            new[] { (HousingId, "Housing", true), (OldId, "Old stuff", false) },
            _api.Updated.Select(u => (u.Id, u.Request.Name, u.Request.IsHidden))));
    }

    [Fact]
    public void Renaming_keeps_whether_it_is_hidden()
    {
        var cut = _context.RenderComponent<Categories>();

        cut.FindAll("[data-testid=rename]").ToList()[2].Click();
        cut.Find("input[data-testid=rename-name]").Change("Older stuff");
        cut.Find("[data-testid=rename-save]").Click();

        cut.WaitForAssertion(() => Assert.Equal((OldId, "Older stuff", true), _api.Updated.Select(u => (u.Id, u.Request.Name, u.Request.IsHidden)).Single()));
    }

    [Fact]
    public void A_refused_delete_is_explained()
    {
        _api.NextResult = new CategoryResult(false, new Dictionary<string, string[]>(), "This category is in use.");
        var cut = _context.RenderComponent<Categories>();

        cut.Find("[data-testid=delete]").Click();

        cut.WaitForAssertion(() => Assert.Contains("This category is in use.", cut.Find("[data-testid=problem]").TextContent, StringComparison.Ordinal));
        Assert.Equal(new[] { PetsId }, _api.Deleted);
    }

    [Fact]
    public void Viewers_can_only_look()
    {
        UseRole("Viewer", isOwner: false);

        var cut = _context.RenderComponent<Categories>();

        Assert.Equal(3, cut.FindAll("[data-testid=category]").Count);
        Assert.Empty(cut.FindAll("[data-testid=add-category]"));
        Assert.Empty(cut.FindAll("[data-testid=rename]"));
        Assert.Empty(cut.FindAll("[data-testid=toggle-hidden]"));
        Assert.Empty(cut.FindAll("[data-testid=delete]"));
    }

    [Fact]
    public void Each_category_shows_its_icon()
    {
        var cut = _context.RenderComponent<Categories>();

        var icons = cut.FindAll("[data-testid=category-icon]").Select(i => i.GetAttribute("data-icon")).ToArray();
        Assert.Equal(new[] { "home", "label", "label" }, icons);
    }

    [Fact]
    public void An_icon_can_be_chosen_when_renaming()
    {
        var cut = _context.RenderComponent<Categories>();

        cut.FindAll("[data-testid=rename]").ToList()[0].Click();
        var picker = cut.Find("[data-testid=icon-picker]");
        Assert.Equal("radiogroup", picker.GetAttribute("role"));
        Assert.Equal("true", cut.Find("[data-testid=icon-option][data-icon=home]").GetAttribute("aria-checked"));

        cut.Find("[data-testid=icon-option][data-icon=bank]").Click();
        Assert.Equal("true", cut.Find("[data-testid=icon-option][data-icon=bank]").GetAttribute("aria-checked"));
        Assert.Equal("false", cut.Find("[data-testid=icon-option][data-icon=home]").GetAttribute("aria-checked"));
        cut.Find("[data-testid=rename-save]").Click();

        cut.WaitForAssertion(() => Assert.Equal(("Housing", "bank"), _api.Updated.Select(u => (u.Request.Name, u.Request.Icon)).Single()));
    }

    [Fact]
    public void A_new_category_sends_the_chosen_icon_or_none_for_the_usual_one()
    {
        var cut = _context.RenderComponent<Categories>();

        cut.Find("input[data-testid=add-name]").Change("Hobbies");
        cut.Find("[data-testid=add-category]").Click();
        cut.WaitForAssertion(() => Assert.Single(_api.AddedIcons));

        cut.Find("input[data-testid=add-name]").Change("Coffee");
        cut.Find("[data-testid=icon-option][data-icon=coffee]").Click();
        cut.Find("[data-testid=add-category]").Click();

        cut.WaitForAssertion(() => Assert.Equal(new string?[] { null, "coffee" }, _api.AddedIcons));
    }

    [Fact]
    public void Every_icon_on_offer_has_its_own_picture_and_a_name()
    {
        Assert.All(CategoryIcons.All, icon =>
        {
            Assert.True(CategoryIconSvg.Has(icon.Key), icon.Key);
            Assert.False(string.IsNullOrWhiteSpace(icon.Label));
        });
        Assert.Equal(CategoryIcons.All.Count, CategoryIcons.All.Select(i => CategoryIconSvg.For(i.Key)).Distinct().Count());
    }

    public void Dispose() => _context.Dispose();

    private void UseRole(string role, bool isOwner = true)
    {
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(role, isOwner));
        _context.Services.AddScoped<UserSession>();
    }

    private sealed class FakeCategoryApi : ICategoryApi
    {
        public List<string> Added { get; } = [];

        public List<string?> AddedIcons { get; } = [];

        public List<(Guid Id, UpdateCategoryRequest Request)> Updated { get; } = [];

        public List<Guid> Deleted { get; } = [];

        public int ListCalls { get; private set; }

        public CategoryResult? NextResult { get; set; }

        public Task<IReadOnlyList<OutgoingCategorySummary>> ListAsync(Guid householdId)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<OutgoingCategorySummary>>(
            [
                new(HousingId, "Housing", IsHidden: false, OutgoingCount: 2, Icon: "home"),
                new(PetsId, "Pets", IsHidden: false, OutgoingCount: 0),
                new(OldId, "Old stuff", IsHidden: true, OutgoingCount: 1),
            ]);
        }

        public Task<CategoryResult> AddAsync(Guid householdId, AddCategoryRequest request)
        {
            if (NextResult is { } result)
            {
                return Task.FromResult(result);
            }

            Added.Add(request.Name);
            AddedIcons.Add(request.Icon);
            return Task.FromResult(CategoryResult.Success);
        }

        public Task<CategoryResult> UpdateAsync(Guid householdId, Guid categoryId, UpdateCategoryRequest request)
        {
            Updated.Add((categoryId, request));
            return Task.FromResult(NextResult ?? CategoryResult.Success);
        }

        public Task<CategoryResult> DeleteAsync(Guid householdId, Guid categoryId)
        {
            Deleted.Add(categoryId);
            return Task.FromResult(NextResult ?? CategoryResult.Success);
        }
    }
}
