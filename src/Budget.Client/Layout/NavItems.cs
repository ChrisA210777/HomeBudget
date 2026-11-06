using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace Budget.Client.Layout;

/// <summary>The main sections, shown in the side drawer on larger screens and the bottom bar on phones.</summary>
public static class NavItems
{
    public static IReadOnlyList<NavItem> All { get; } =
    [
        new("Home", "/", Icons.Material.Filled.Home, NavLinkMatch.All),
        new("Budget", "/budget", Icons.Material.Filled.AccountBalanceWallet, NavLinkMatch.Prefix),
        new("Income", "/income", Icons.Material.Filled.Payments, NavLinkMatch.Prefix),
        new("Outgoings", "/outgoings", Icons.Material.Filled.ReceiptLong, NavLinkMatch.Prefix),
        new("People", "/people", Icons.Material.Filled.People, NavLinkMatch.Prefix),
    ];
}

public sealed record NavItem(string Label, string Href, string Icon, NavLinkMatch Match);
