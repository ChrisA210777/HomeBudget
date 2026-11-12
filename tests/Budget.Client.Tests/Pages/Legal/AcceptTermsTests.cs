using Budget.Client.Account;
using Budget.Client.Pages.Legal;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Account;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Legal;

/// <summary>Accepting the terms of use (BUD-152).</summary>
public sealed class AcceptTermsTests : IDisposable
{
    private readonly TestContext _context = new();
    private readonly FakeTermsApi _terms = new();
    private readonly FakeAccountApi _account = new(mustAcceptTerms: true);

    public AcceptTermsTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<ITermsApi>(_terms);
        _context.Services.AddSingleton<IAccountApi>(_account);
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void Accepting_needs_the_box_ticked_then_records_the_version_and_goes_back()
    {
        var navigation = _context.Services.GetRequiredService<FakeNavigationManager>();
        navigation.NavigateTo("/terms/accept?returnUrl=%2Foutgoings");
        var cut = _context.RenderComponent<AcceptTerms>();

        Assert.Contains("Version " + LegalDocuments.TermsVersion, cut.Find("[data-testid=legal-version]").TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find("[data-testid=accept-terms]").HasAttribute("disabled"));

        cut.Find("[data-testid=accept-agree] input").Change(true);
        cut.Find("[data-testid=accept-terms]").Click();

        cut.WaitForAssertion(() => Assert.Equal(LegalDocuments.TermsVersion, _terms.Accepted));
        Assert.Equal("http://localhost/outgoings", navigation.Uri);
        var user = _context.Services.GetRequiredService<UserSession>().GetCurrentUserAsync().Result;
        Assert.False(user!.MustAcceptTerms);
    }

    [Theory]
    [InlineData("https://elsewhere.example/")]
    [InlineData("//elsewhere.example/")]
    public void Only_goes_back_to_a_page_in_the_app(string returnUrl)
    {
        var navigation = _context.Services.GetRequiredService<FakeNavigationManager>();
        navigation.NavigateTo("/terms/accept?returnUrl=" + Uri.EscapeDataString(returnUrl));
        var cut = _context.RenderComponent<AcceptTerms>();

        cut.Find("[data-testid=accept-agree] input").Change(true);
        cut.Find("[data-testid=accept-terms]").Click();

        cut.WaitForAssertion(() => Assert.Equal("http://localhost/", navigation.Uri));
    }

    [Fact]
    public void Not_accepting_lets_you_sign_out()
    {
        var cut = _context.RenderComponent<AcceptTerms>();

        cut.Find("[data-testid=accept-sign-out]").Click();

        cut.WaitForAssertion(() => Assert.True(_account.SignedOut));
        Assert.Null(_terms.Accepted);
    }

    public void Dispose() => _context.Dispose();

    private sealed class FakeTermsApi : ITermsApi
    {
        public string? Accepted { get; private set; }

        public Task<string?> AcceptAsync(string version)
        {
            Accepted = version;
            return Task.FromResult<string?>(null);
        }
    }
}
