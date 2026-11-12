namespace Budget.Client;

/// <summary>
/// "Back means back" (BUD-117): links to an outgoing, income or transfer say which page they came from in
/// <c>?from=</c>, filters included, and the item's back button returns there. Opened any other way (a bookmark, a
/// refresh), back goes to the item's list.
/// </summary>
public static class BackLink
{
    public const string Param = "from";

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/"] = "Home",
        ["/budget"] = "Budget",
        ["/outgoings"] = "Outgoings",
        ["/outgoings/upcoming"] = "Upcoming payments",
        ["/outgoings/renewals"] = "Renewals",
        ["/outgoings/categories"] = "Categories",
        ["/income"] = "Income",
        ["/accounts"] = "Accounts",
        ["/transfers"] = "Transfers",
        ["/cash-flow"] = "Cash flow",
        ["/activity"] = "Recent activity",
    };

    /// <summary><paramref name="href"/>, remembering <paramref name="from"/> as the way back. Unchanged if there's no safe origin.</summary>
    public static string To(string href, string? from) =>
        Safe(from) is { } origin ? $"{href}{(href.Contains('?', StringComparison.Ordinal) ? '&' : '?')}{Param}={Uri.EscapeDataString(origin)}" : href;

    /// <summary>Where back goes: the origin if there's a safe one, otherwise <paramref name="fallback"/>.</summary>
    public static string Resolve(string? from, string fallback) => Safe(from) ?? fallback;

    /// <summary>The page's name for a back button, e.g. "Upcoming payments", or <c>null</c> if it isn't one we know.</summary>
    public static string? Name(string href)
    {
        var path = href.Split('?', '#')[0];
        path = path.Length > 1 ? path.TrimEnd('/') : path;
        return Names.TryGetValue(path, out var name) ? name : null;
    }

    /// <summary>Only a path in this app: never another site, so a crafted link can't send people elsewhere.</summary>
    public static string? Safe(string? from) =>
        from is { Length: > 0 }
        && from[0] == '/'
        && !from.StartsWith("//", StringComparison.Ordinal)
        && !from.Contains('\\', StringComparison.Ordinal)
        && !from.Contains(':', StringComparison.Ordinal)
            ? from
            : null;
}
