using Budget.Client.Account;
using Budget.Client.Layout;
using Budget.Client.Tests.TestDoubles;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Budget.Client.Tests.Layout;

public sealed class MainLayoutTests : IDisposable
{
    private readonly TestContext _context = new();
    private readonly TestAuthorizationContext _auth;

    public MainLayoutTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi());
        _context.Services.AddSingleton<Budget.Client.Households.IHouseholdApi>(new RecordingHouseholdApi());
        _context.Services.AddScoped<UserSession>();
        _context.Services.AddScoped<ThemePreference>();
        _context.Services.AddScoped<Connectivity>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _auth = _context.AddTestAuthorization();
        _auth.SetAuthorized("Chris Austin");
    }

    private Connectivity Connectivity => _context.Services.GetRequiredService<Connectivity>();

    [Fact]
    public void Shows_the_household_name_in_the_app_bar_and_keeps_it_up_to_date()
    {
        var cut = Render();

        Assert.Equal("My Household", cut.Find("[data-testid=appbar-household]").TextContent.Trim());

        cut.InvokeAsync(() => _context.Services.GetRequiredService<UserSession>()
            .SetHouseholdNameAsync(FakeAccountApi.HouseholdId, "Smith Family"));

        cut.WaitForAssertion(() => Assert.Equal("Smith Family", cut.Find("[data-testid=appbar-household]").TextContent.Trim()));
    }

    [Fact]
    public void Groups_the_side_drawer_and_keeps_the_phone_bar_to_the_main_sections()
    {
        var cut = Render();

        cut.WaitForAssertion(() => Assert.Equal(
            new[] { "/", "/budget", "/outgoings/upcoming", "/outgoings/renewals", "/accounts", "/income", "/outgoings", "/people", "/members", "/activity", "/household/data" },
            cut.FindAll("[data-testid=side-nav] a").Select(a => a.GetAttribute("href")).ToArray()));
        Assert.Equal(new[] { "/", "/budget", "/income", "/outgoings", "/more" }, cut.FindAll("[data-testid=bottom-nav] a").Select(a => a.GetAttribute("href")).ToArray());
        Assert.Equal(new[] { "Home", "Budget", "Income", "Outgoings", "More" }, cut.FindAll("[data-testid=bottom-nav] a").Select(a => a.TextContent.Trim()).ToArray());
    }

    [Fact]
    public void Highlights_where_you_are_in_both_navigations()
    {
        var navigation = _context.Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>();
        var cut = Render();

        navigation.NavigateTo("/outgoings/renewals");
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("/outgoings/renewals", cut.Find("[data-testid=side-nav] a[aria-current=page]").GetAttribute("href"));
            Assert.Equal("/more", cut.Find("[data-testid=bottom-nav] a[aria-current=page]").GetAttribute("href"));
        });

        navigation.NavigateTo("/outgoings/" + Guid.NewGuid());
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("/outgoings", cut.Find("[data-testid=side-nav] a[aria-current=page]").GetAttribute("href"));
            Assert.Equal("/outgoings", cut.Find("[data-testid=bottom-nav] a[aria-current=page]").GetAttribute("href"));
        });
    }

    [Fact]
    public void Signed_out_people_see_no_navigation()
    {
        _auth.SetNotAuthorized();

        var cut = Render();

        Assert.Empty(cut.FindAll("[data-testid=side-nav]").ToList());
        Assert.Empty(cut.FindAll("[data-testid=bottom-nav]").ToList());
        Assert.Empty(cut.FindAll("[data-testid=drawer-toggle]").ToList());
    }

    [Fact]
    public void Shows_a_banner_while_offline_and_tells_pages_they_cant_save()
    {
        var cut = Render(OnlineProbe);
        Assert.Empty(cut.FindAll("[data-testid=offline-banner]").ToList());
        Assert.Equal("online", cut.Find("[data-testid=probe]").TextContent);

        cut.InvokeAsync(() => Connectivity.OnConnectivityChanged(online: false));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("You're offline", cut.Find("[data-testid=offline-banner]").TextContent, StringComparison.Ordinal);
            Assert.Equal("offline", cut.Find("[data-testid=probe]").TextContent);
        });

        cut.InvokeAsync(() => Connectivity.OnConnectivityChanged(online: true));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=offline-banner]").ToList()));
    }

    [Fact]
    public void Reloads_the_page_when_the_connection_comes_back()
    {
        var created = 0;
        var cut = Render(builder =>
        {
            builder.OpenComponent<CountingPage>(0);
            builder.AddAttribute(1, nameof(CountingPage.OnCreated), (Action)(() => created++));
            builder.CloseComponent();
        });
        Assert.Equal(1, created);

        cut.InvokeAsync(() => Connectivity.OnConnectivityChanged(online: false));
        cut.InvokeAsync(() => Connectivity.OnConnectivityChanged(online: true));

        cut.WaitForAssertion(() => Assert.Equal(2, created));
    }

    [Fact]
    public void Offers_to_reload_when_a_new_version_is_ready()
    {
        var cut = Render();
        Assert.Empty(cut.FindAll("[data-testid=update-banner]").ToList());

        cut.InvokeAsync(Connectivity.OnUpdateAvailable);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("new version", cut.Find("[data-testid=update-banner]").TextContent, StringComparison.Ordinal);
            Assert.NotNull(cut.Find("[data-testid=update-reload]"));
        });
    }

    [Fact]
    public void A_page_that_fails_to_load_shows_a_friendly_message_with_retry()
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent<FailingPage>(0);
            builder.CloseComponent();
        });

        Assert.Contains("Something went wrong loading this page.", cut.Find("[data-testid=page-error]").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid=page-retry]"));
        Assert.Empty(cut.FindAll("[data-testid=error-reference]"));
    }

    [Fact]
    public void A_server_error_shows_its_reference_to_quote()
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent<ServerFailingPage>(0);
            builder.CloseComponent();
        });

        Assert.Contains("4bf92f3577b34da6a3ce929d0e0e4736", cut.Find("[data-testid=error-reference]").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ThemeMode.Dark, true)]
    [InlineData(ThemeMode.Light, false)]
    [InlineData(ThemeMode.System, false)]
    public void Applies_the_chosen_theme(ThemeMode mode, bool dark)
    {
        var cut = Render();

        cut.InvokeAsync(() => _context.Services.GetRequiredService<ThemePreference>().SetAsync(mode));

#pragma warning disable MUD0012 // Reading the parameter is the point of this test.
        cut.WaitForAssertion(() => Assert.Equal(dark, cut.FindComponent<MudThemeProvider>().Instance.IsDarkMode));
#pragma warning restore MUD0012
    }

    [Fact]
    public void Someone_who_hasnt_accepted_the_terms_is_asked_to_first_then_brought_back()
    {
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(mustAcceptTerms: true));
        var navigation = _context.Services.GetRequiredService<FakeNavigationManager>();
        navigation.NavigateTo("/outgoings?view=year");

        Render();

        navigation.WaitForAssertion(() => Assert.Equal("http://localhost/terms/accept?returnUrl=%2Foutgoings%3Fview%3Dyear", navigation.Uri));
    }

    [Theory]
    [InlineData("/terms")]
    [InlineData("/privacy")]
    [InlineData("/settings")]
    public void The_terms_privacy_notice_and_settings_open_without_accepting(string page)
    {
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi(mustAcceptTerms: true));
        var navigation = _context.Services.GetRequiredService<FakeNavigationManager>();
        navigation.NavigateTo(page);

        Render();

        Assert.Equal("http://localhost" + page, navigation.Uri);
    }

    [Fact]
    public void Someone_who_has_accepted_isnt_asked()
    {
        var navigation = _context.Services.GetRequiredService<FakeNavigationManager>();
        navigation.NavigateTo("/outgoings");

        Render();

        Assert.Equal("http://localhost/outgoings", navigation.Uri);
    }

    public void Dispose() => _context.Dispose();

    private static void OnlineProbe(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
    {
        builder.OpenComponent<OnlineProbeComponent>(0);
        builder.CloseComponent();
    }

    private IRenderedComponent<MainLayout> Render(RenderFragment? body = null) =>
        _context.RenderComponent<MainLayout>(p => p.Add(l => l.Body, body ?? (b => b.AddContent(0, "page"))));

    private sealed class OnlineProbeComponent : ComponentBase
    {
        [CascadingParameter(Name = "Online")]
        public bool Online { get; set; } = true;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "data-testid", "probe");
            builder.AddContent(2, Online ? "online" : "offline");
            builder.CloseElement();
        }
    }

    private sealed class CountingPage : ComponentBase
    {
        [Parameter]
        public Action? OnCreated { get; set; }

        protected override void OnInitialized() => OnCreated?.Invoke();
    }

    private sealed class FailingPage : ComponentBase
    {
        protected override void OnInitialized() => throw new HttpRequestException("offline");
    }

    private sealed class ServerFailingPage : ComponentBase
    {
        protected override void OnInitialized() =>
            throw new ServerErrorException(System.Net.HttpStatusCode.InternalServerError, "4bf92f3577b34da6a3ce929d0e0e4736");
    }
}
