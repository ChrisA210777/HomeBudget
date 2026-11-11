using Budget.Shared.Account;
using MudBlazor;

namespace Budget.Client.Layout;

/// <summary>
/// Where every page sits in the navigation (BUD-108, BUD-115, BUD-116). Larger screens list everything in the side drawer;
/// phones have a bottom bar of the most used sections plus "More", which lists the rest.
/// </summary>
public static class NavItems
{
    public const string MoreHref = "/more";

    private static readonly NavItem Home = new("Home", "/", Icons.Material.Filled.Home);
    private static readonly NavItem Budget = new("Budget", "/budget", Icons.Material.Filled.AccountBalanceWallet);
    private static readonly NavItem Upcoming = new("Upcoming payments", "/outgoings/upcoming", Icons.Material.Filled.Event);
    private static readonly NavItem Income = new("Income", "/income", Icons.Material.Filled.Payments);
    private static readonly NavItem Outgoings = new("Outgoings", "/outgoings", Icons.Material.Filled.ReceiptLong);
    private static readonly NavItem Renewals = new("Renewals", "/outgoings/renewals", Icons.Material.Filled.EventRepeat, Sub: true);
    private static readonly NavItem Categories = new("Categories", "/outgoings/categories", Icons.Material.Filled.Category, Sub: true);
    private static readonly NavItem Accounts = new("Accounts", "/accounts", Icons.Material.Filled.AccountBalance);
    private static readonly NavItem Transfers = new("Transfers", "/transfers", Icons.Material.Filled.SwapHoriz, Sub: true);
    private static readonly NavItem CashFlow = new("Cash flow", "/cash-flow", Icons.Material.Filled.Timeline, Sub: true);
    private static readonly NavItem People = new("People", "/people", Icons.Material.Filled.People);
    private static readonly NavItem Members = new("Members", "/members", Icons.Material.Filled.ManageAccounts);
    private static readonly NavItem Data = new("Download or import data", "/household/data", Icons.Material.Filled.ImportExport);
    private static readonly NavItem Settings = new("Settings", "/settings", Icons.Material.Filled.Settings);
    private static readonly NavItem More = new("More", MoreHref, Icons.Material.Filled.Menu);

    /// <summary>The phone's bottom bar.</summary>
    public static IReadOnlyList<NavItem> PhoneBar { get; } = [Home, Budget, Income, Outgoings, More];

    /// <summary>The side drawer, in groups. Settings is in the account menu.</summary>
    public static IReadOnlyList<NavSection> Drawer(HouseholdMembershipSummary? household) =>
    [
        new(null, [Home, Budget, Upcoming, Income]),
        new(null, [Outgoings, Renewals, Categories, .. AccountItems(household)]),
        new("Household", HouseholdItems(household)),
    ];

    /// <summary>The phone's "More" page: everything not in the bottom bar.</summary>
    public static IReadOnlyList<NavSection> MorePage(HouseholdMembershipSummary? household) =>
    [
        new(null, [Upcoming]),
        new("Outgoings", [Unindented(Renewals), Unindented(Categories)]),
        new("Accounts", [.. AccountItems(household).Select(Unindented)]),
        new("Household", HouseholdItems(household)),
        new("You", [Settings]),
    ];

    /// <summary>
    /// The item to highlight for a page: the one whose address is the longest match for it, so an outgoing's page
    /// highlights Outgoings and Renewals highlights Renewals. Home only matches itself.
    /// </summary>
    public static string? ActiveHref(string path, IEnumerable<string> hrefs)
    {
        var bare = "/" + path.Split('?', '#')[0].Trim('/');
        return hrefs
            .Where(href => href == "/" ? bare == "/" : bare == href || bare.StartsWith(href + "/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(href => href.Length)
            .FirstOrDefault();
    }

    /// <summary>The bottom-bar item to highlight: a section in the bar, or More for a page listed on it.</summary>
    public static string? ActivePhoneHref(string path, HouseholdMembershipSummary? household)
    {
        var all = Drawer(household).SelectMany(s => s.Items).Append(Settings).Append(More).Select(i => i.Href).ToList();
        var active = ActiveHref(path, all);
        if (active is null)
        {
            return null;
        }

        return PhoneBar.Any(i => i.Href == active) ? active : MoreHref;
    }

    private static IEnumerable<NavItem> AccountItems(HouseholdMembershipSummary? household) =>
        household?.TransfersEnabled == true ? [Accounts, Transfers, CashFlow] : [Accounts];

    private static IReadOnlyList<NavItem> HouseholdItems(HouseholdMembershipSummary? household) =>
        household?.Role == "Admin" ? [People, Members, Data] : [People, Members];

    private static NavItem Unindented(NavItem item) => item with { Sub = false };
}

/// <param name="Sub">Shown indented under the item before it, in the side drawer.</param>
public sealed record NavItem(string Label, string Href, string Icon, bool Sub = false);

/// <param name="Title">A heading for the group, if it has one.</param>
public sealed record NavSection(string? Title, IReadOnlyList<NavItem> Items);
