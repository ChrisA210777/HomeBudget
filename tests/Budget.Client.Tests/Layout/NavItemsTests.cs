using Budget.Client.Layout;
using Budget.Shared.Account;

namespace Budget.Client.Tests.Layout;

public sealed class NavItemsTests
{
    private static HouseholdMembershipSummary Household(string role = "Admin", bool transfers = false) =>
        new(Guid.NewGuid(), "Home", role, IsOwner: false, TransfersEnabled: transfers);

    [Fact]
    public void The_drawer_has_home_then_planning_money_and_household()
    {
        var sections = NavItems.Drawer(Household(transfers: true));

        Assert.Equal(new string?[] { null, "Planning", "Money", "Household" }, sections.Select(s => s.Title).ToArray());
        Assert.Equal(
            new[] { "Home", "Budget", "Upcoming payments", "Renewals", "Cash flow", "Accounts", "Income", "Outgoings", "People", "Members", "Recent activity", "Import or export data" },
            sections.SelectMany(s => s.Items).Select(i => i.Label).ToArray());
    }

    [Theory]
    [InlineData("", "/")]
    [InlineData("budget", "/budget")]
    [InlineData("outgoings", "/outgoings")]
    [InlineData("outgoings?kind=Bill", "/outgoings")]
    [InlineData("outgoings/0b4c2f6e-0000-0000-0000-000000000000/edit", "/outgoings")]
    [InlineData("outgoings/categories", "/outgoings")]
    [InlineData("outgoings/upcoming", "/outgoings/upcoming")]
    [InlineData("outgoings/renewals", "/outgoings/renewals")]
    [InlineData("income/new", "/income")]
    [InlineData("transfers", "/accounts")]
    [InlineData("transfers/new", "/accounts")]
    [InlineData("households/new", null)]
    [InlineData("budgeting", null)]
    public void The_longest_matching_item_is_highlighted(string path, string? expected) =>
        Assert.Equal(expected, NavItems.ActiveHref(path, NavItems.Drawer(Household(transfers: true)).SelectMany(s => s.Items)));

    [Theory]
    [InlineData("outgoings/1", "/outgoings")]
    [InlineData("outgoings/categories", "/outgoings")]
    [InlineData("outgoings/upcoming", "/more")]
    [InlineData("accounts", "/more")]
    [InlineData("transfers/1", "/more")]
    [InlineData("settings", "/more")]
    [InlineData("activity", "/more")]
    [InlineData("more", "/more")]
    [InlineData("", "/")]
    public void On_phones_pages_listed_on_More_highlight_More(string path, string expected) =>
        Assert.Equal(expected, NavItems.ActivePhoneHref(path, Household(transfers: true)));

    [Fact]
    public void Cash_flow_only_shows_when_transfers_are_turned_on()
    {
        static string[] Hrefs(HouseholdMembershipSummary h) => NavItems.Drawer(h).SelectMany(s => s.Items).Select(i => i.Href).ToArray();

        Assert.DoesNotContain("/cash-flow", Hrefs(Household()));
        Assert.Contains("/cash-flow", Hrefs(Household(transfers: true)));
        Assert.DoesNotContain("/transfers", Hrefs(Household(transfers: true)));
    }

    [Fact]
    public void Only_admins_see_import_or_export()
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
        Assert.Equal(new[] { "Planning", "Money", "Household", "You" }, NavItems.MorePage(household).Select(s => s.Title).ToArray());
    }
}
