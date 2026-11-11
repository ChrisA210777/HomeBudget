using Budget.Client.Layout;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Services;

namespace Budget.Client.Tests.Layout;

public sealed class PeriodNavigatorTests : IDisposable
{
    private readonly TestContext _context = new();

    public PeriodNavigatorTests()
    {
        _context.Services.AddMudServices();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Without_choices_the_name_is_plain_text()
    {
        var cut = _context.RenderComponent<PeriodNavigator>(p => p.Add(n => n.Name, "November 2026"));

        Assert.Equal("November 2026", cut.Find("[data-testid=period-name]").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid=period-name] button"));
    }

    [Fact]
    public void With_choices_the_name_opens_them_as_a_menu()
    {
        var popovers = _context.RenderComponent<MudPopoverProvider>();
        var chosen = new List<string>();
        var cut = _context.RenderComponent<PeriodNavigator>(p => p
            .Add(n => n.Name, "9 – 15 Nov 2026")
            .Add(n => n.Choices, new PeriodChoice[]
            {
                new("A week", "show-week", true, EventCallback.Factory.Create(this, () => chosen.Add("week"))),
                new("A month", "show-month", false, EventCallback.Factory.Create(this, () => chosen.Add("month"))),
            }));

        var button = cut.Find("[data-testid=period-name] button");
        Assert.Equal("Change period, 9 – 15 Nov 2026", button.GetAttribute("aria-label"));
        Assert.Equal("9 – 15 Nov 2026", cut.Find("[data-testid=period-name]").TextContent.Trim());

        button.Click();
        popovers.WaitForElement("[data-testid=show-week]");
        popovers.Find("[data-testid=show-month]").Click();
        Assert.Equal(new[] { "month" }, chosen);
    }
}
