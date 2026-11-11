using Budget.Client.Layout;
using Budget.Shared.Account;

namespace Budget.Client.Tests.Layout;

public sealed class NavItemsTests
{
    private static HouseholdMembershipSummary Household(string role = "Admin", bool transfers = false) =>
        new(Guid.NewGuid(), "Home", role, IsOwner: false, TransfersEnabled: transfers);

    [Theory]
    [InlineData("", "/")]
    [InlineData("budget", "/budget")]
    [InlineData("outgoings", "/outgoings")]
    [InlineData("outgoings?kind=Bill", "/outgoings")]
    [InlineData("outgoings/0b4c2f6e-0000-0000-0000-000000000000/edit", "/outgoings")]
    [InlineData("outgoings/upcoming", "/outgoings/upcoming")]
    [InlineData("outgoings/renewals", "/outgoings/renewals")]
    [InlineData("income/new", "/income")]
    [InlineData("transfers/new", "/transfers")]
    [InlineData("households/new", null)]
    [InlineData("budgeting", null)]
    public void The_longest_matching_item_is_highlighted(string path, string? expected)
    {
        var hrefs = NavItems.Drawer(Household(transfers: true)).SelectMany(s => s.Items).Select(i => i.Href);

        Assert.Equal(expected, NavItems.ActiveHref(path, hrefs));
    }

    [Theory]
    [InlineData("outgoings/1", "/outgoings")]
    [InlineData("outgoings/upcoming", "/more")]
    [InlineData("accounts", "/more")]
    [InlineData("settings", "/more")]
    [InlineData("more", "/more")]
    [InlineData("", "/")]
    public void On_phones_pages_listed_on_More_highlight_More(string path, string expected) =>
        Assert.Equal(expected, NavItems.ActivePhoneHref(path, Household(transfers: true)));

    [Fact]
    public void Transfers_and_cash_flow_only_show_when_transfers_are_turned_on()
    {
        static string[] Hrefs(HouseholdMembershipSummary h) => NavItems.Drawer(h).SelectMany(s => s.Items).Select(i => i.Href).ToArray();

        Assert.DoesNotContain("/transfers", Hrefs(Household()));
        Assert.Contains("/transfers", Hrefs(Household(transfers: true)));
        Assert.Contains("/cash-flow", Hrefs(Household(transfers: true)));
    }

    [Fact]
    public void Only_admins_see_download_or_import()
    {
        Assert.Contains(NavItems.Drawer(Household()).SelectMany(s => s.Items), i => i.Href == "/household/data");
        Assert.DoesNotContain(NavItems.Drawer(Household("User")).SelectMany(s => s.Items), i => i.Href == "/household/data");
        Assert.DoesNotContain(NavItems.MorePage(Household("Viewer")).SelectMany(s => s.Items), i => i.Href == "/household/data");
    }

    [Fact]
    public void More_lists_everything_not_in_the_phone_bar_plus_settings()
    {
        var household = Household(transfers: true);
        var bar = NavItems.PhoneBar.Select(i => i.Href).ToHashSet();
        var drawerOnly = NavItems.Drawer(household).SelectMany(s => s.Items).Select(i => i.Href).Where(h => !bar.Contains(h));

        var more = NavItems.MorePage(household).SelectMany(s => s.Items).Select(i => i.Href).ToList();

        Assert.Equal(drawerOnly.Append("/settings").Order(), more.Order());
        Assert.All(NavItems.MorePage(household).SelectMany(s => s.Items), i => Assert.False(i.Sub));
    }
}
