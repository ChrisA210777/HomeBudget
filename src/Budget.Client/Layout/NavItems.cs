using Budget.Shared.Account;
using MudBlazor;

namespace Budget.Client.Layout;

/// <summary>
/// Where every page sits in the navigation (BUD-108, BUD-115, BUD-116, BUD-119). Larger screens list everything in the side drawer;
/// phones have a bottom bar of the most used sections plus "More", which lists the rest.
/// </summary>
public static class NavItems
{
    public const string MoreHref = "/more";

    private static readonly NavItem Home = new("Home", "/", Icons.Material.Filled.Home);
    private static readonly NavItem Budget = new("Budget", "/budget", Icons.Material.Filled.AccountBalanceWallet);
    private static readonly NavItem Upcoming = new("Upcoming payments", "/outgoings/upcoming", Icons.Material.Filled.Event);
    private static readonly NavItem Renewals = new("Renewals", "/outgoings/renewals", Icons.Material.Filled.EventRepeat);
    private static readonly NavItem CashFlow = new("Cash flow", "/cash-flow", Icons.Material.Filled.Timeline);
    private static readonly NavItem Accounts = new("Accounts", "/accounts", Icons.Material.Filled.AccountBalance, Also: ["/transfers"]);
    private static readonly NavItem Income = new("Income", "/income", Icons.Material.Filled.Payments);
    private static readonly NavItem Outgoings = new("Outgoings", "/outgoings", Icons.Material.Filled.ReceiptLong);
    private static readonly NavItem People = new("People", "/people", Icons.Material.Filled.People);
    private static readonly NavItem Members = new("Members", "/members", Icons.Material.Filled.ManageAccounts);
    private static readonly NavItem Data = new("Import or export data", "/household/data", Icons.Material.Filled.ImportExport);
    private static readonly NavItem Settings = new("Settings", "/settings", Icons.Material.Filled.Settings);
    private static readonly NavItem More = new("More", MoreHref, Icons.Material.Filled.Menu);

    /// <summary>The phone's bottom bar.</summary>
    public static IReadOnlyList<NavItem> PhoneBar { get; } = [Home, Budget, Income, Outgoings, More];

    /// <summary>
    /// The side drawer (BUD-119): Home, then what you check (Planning), what you keep up to date (Money) and the
    /// household itself. Categories and Transfers are linked from Outgoings and Accounts. Settings is in the account menu.
    /// </summary>
    public static IReadOnlyList<NavSection> Drawer(HouseholdMembershipSummary? household) =>
    [
        new(null, [Home]),
        new("Planning", PlanningItems(household)),
        new("Money", [Accounts, Income, Outgoings]),
        new("Household", HouseholdItems(household)),
    ];

    /// <summary>The phone's "More" page: everything not in the bottom bar, in the drawer's groups.</summary>
    public static IReadOnlyList<NavSection> MorePage(HouseholdMembershipSummary? household) =>
    [
        new("Planning", [.. PlanningItems(household).Where(i => i != Budget)]),
        new("Money", [Accounts]),
        new("Household", HouseholdItems(household)),
        new("You", [Settings]),
    ];

    /// <summary>
    /// The item to highlight for a page: the one whose address (or one it also covers) is the longest match, so an
    /// outgoing's page and Categories highlight Outgoings, Renewals highlights Renewals and Transfers highlights
    /// Accounts. Home only matches itself.
    /// </summary>
    public static string? ActiveHref(string path, IEnumerable<NavItem> items)
    {
        var bare = "/" + path.Split('?', '#')[0].Trim('/');
        return items
            .SelectMany(item => item.Also.Prepend(item.Href).Select(prefix => (item.Href, Prefix: prefix)))
            .Where(m => m.Prefix == "/" ? bare == "/" : bare == m.Prefix || bare.StartsWith(m.Prefix + "/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Prefix.Length)
            .Select(m => m.Href)
            .FirstOrDefault();
    }

    /// <summary>The bottom-bar item to highlight: a section in the bar, or More for a page listed on it.</summary>
    public static string? ActivePhoneHref(string path, HouseholdMembershipSummary? household)
    {
        var active = ActiveHref(path, Drawer(household).SelectMany(s => s.Items).Append(Settings).Append(More));
        if (active is null)
        {
            return null;
        }

        return PhoneBar.Any(i => i.Href == active) ? active : MoreHref;
    }

    private static IReadOnlyList<NavItem> PlanningItems(HouseholdMembershipSummary? household) =>
        household?.TransfersEnabled == true ? [Budget, Upcoming, Renewals, CashFlow] : [Budget, Upcoming, Renewals];

    private static IReadOnlyList<NavItem> HouseholdItems(HouseholdMembershipSummary? household) =>
        household?.Role == "Admin" ? [People, Members, Data] : [People, Members];
}

/// <param name="Also">Other pages it's highlighted for, e.g. Accounts for Transfers.</param>
public sealed record NavItem(string Label, string Href, string Icon, string[]? Also = null)
{
    public string[] Also { get; init; } = Also ?? [];
}

/// <param name="Title">A heading for the group, if it has one.</param>
public sealed record NavSection(string? Title, IReadOnlyList<NavItem> Items);
