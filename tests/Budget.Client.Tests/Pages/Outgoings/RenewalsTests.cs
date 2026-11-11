using Budget.Client.Account;
using Budget.Client.Outgoings;
using Budget.Client.Pages.Outgoings;
using Budget.Client.Tests.TestDoubles;
using Budget.Shared.Outgoings;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Budget.Client.Tests.Pages.Outgoings;

/// <summary>The renewals calendar (BUD-81).</summary>
public sealed class RenewalsTests : IDisposable
{
    private readonly TestContext _context = new();
    private readonly FakeRenewalApi _api = new();

    public RenewalsTests()
    {
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<IRenewalApi>(_api);
        _context.Services.AddSingleton<IAccountApi>(new FakeAccountApi("Viewer", isOwner: false));
        _context.Services.AddScoped<UserSession>();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.AddTestAuthorization().SetAuthorized("Chris Austin");
    }

    [Fact]
    public void Lists_renewals_by_month_with_anniversaries_and_pauses_marked()
    {
        _api.Calendar = new RenewalsResponse(new DateOnly(2026, 11, 9), new DateOnly(2027, 11, 8), "GBP",
        [
            Entry(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20), 0, "Car insurance", 480m, PaymentUnitDto.Year, "Joint bills", autoRenews: true),
            Entry(new DateOnly(2026, 11, 28), new DateOnly(2026, 11, 28), 0, "Phone contract", 25m, PaymentUnitDto.Month, null, paused: true) with { PersonName = "Jane" },
            Entry(new DateOnly(2027, 3, 15), new DateOnly(2029, 3, 15), 2, "Mortgage", 900m, PaymentUnitDto.Month, "Joint bills"),
        ]);

        var cut = _context.RenderComponent<Renewals>();

        Assert.Equal(new[] { "November 2026", "March 2027" }, Texts(cut, "renewal-month-name"));
        Assert.Equal(new[] { "Car insurance", "Phone contract", "Mortgage" }, Texts(cut, "renewal-name"));
        Assert.Equal(new[] { "20", "28", "15" }, Texts(cut, "renewal-day"));
        // The weekday and date are centred in their column.
        Assert.All(cut.FindAll("[data-testid=renewal-day], [data-testid=renewal-weekday]"), e => Assert.Contains("mud-typography-align-center", e.ClassName, StringComparison.Ordinal));
        Assert.Equal(new[] { "£480.00", "£25.00", "£900.00" }, Texts(cut, "renewal-amount"));
        Assert.Equal("Yearly · Insurance · from Joint bills", Texts(cut, "renewal-details")[0]);
        Assert.Equal("Monthly · Insurance", Texts(cut, "renewal-details")[1]);
        Assert.Equal(new[] { "Auto-renews" }, Texts(cut, "renewal-auto"));
        Assert.Equal(new[] { "Jane" }, Texts(cut, "renewal-person"));
        Assert.Equal("For Jane", cut.Find("[data-testid=renewal-person]").GetAttribute("aria-label"));
        Assert.Equal(new[] { "Renews in 2 years" }, Texts(cut, "renewal-years"));
        Assert.Equal("Renews on 15 March 2029", cut.Find("[data-testid=renewal-years]").GetAttribute("title"));
        Assert.Single(cut.FindAll("[data-testid=renewal-paused]"));
    }

    [Fact]
    public void Says_what_to_do_when_nothing_renews()
    {
        var cut = _context.RenderComponent<Renewals>();

        Assert.Contains("Nothing renews in the next 12 months", cut.Find("[data-testid=renewals-empty]").TextContent, StringComparison.Ordinal);
    }

    public void Dispose() => _context.Dispose();

    private static string[] Texts(IRenderedComponent<Renewals> cut, string testId) =>
        cut.FindAll($"[data-testid={testId}]").Select(e => e.TextContent.Trim()).ToArray();

    private static RenewalEntry Entry(
        DateOnly date, DateOnly renewsOn, int years, string name, decimal amount, PaymentUnitDto unit, string? paidFrom, bool autoRenews = false, bool paused = false) =>
        new(date, renewsOn, years, Guid.NewGuid(), name, "Insurance", "shield", amount, 1, unit, paidFrom, autoRenews, paused);
}
