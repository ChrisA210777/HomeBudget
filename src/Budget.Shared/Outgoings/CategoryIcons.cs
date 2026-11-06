namespace Budget.Shared.Outgoings;

/// <summary>A category icon: a short key that's stored, and the name screen readers and the picker use.</summary>
public sealed record CategoryIcon(string Key, string Label);

/// <summary>
/// The icons a category can have (BUD-69). Only the key is stored; the client turns it into a Material icon.
/// Keys are never renamed or removed, because households have saved them.
/// </summary>
public static class CategoryIcons
{
    public const string Default = "label";

    public const int KeyMaxLength = 30;

    public static IReadOnlyList<CategoryIcon> All { get; } =
    [
        new("home", "Home"),
        new("bolt", "Energy"),
        new("water", "Water"),
        new("wifi", "Internet"),
        new("phone", "Phone"),
        new("shield", "Insurance"),
        new("build", "Repairs"),
        new("garden", "Garden"),
        new("cleaning", "Cleaning"),
        new("car", "Car"),
        new("fuel", "Fuel"),
        new("train", "Travel"),
        new("flight", "Flights"),
        new("holiday", "Holidays"),
        new("pets", "Pets"),
        new("health", "Health"),
        new("care", "Personal care"),
        new("fitness", "Fitness"),
        new("cart", "Shopping"),
        new("restaurant", "Eating out"),
        new("coffee", "Coffee"),
        new("child", "Children"),
        new("school", "School"),
        new("tv", "TV"),
        new("subscriptions", "Subscriptions"),
        new("music", "Music"),
        new("games", "Games"),
        new("books", "Books"),
        new("computer", "Computer"),
        new("ticket", "Entertainment"),
        new("celebration", "Celebrations"),
        new("gift", "Gifts"),
        new("charity", "Charity"),
        new("bank", "Bank"),
        new("card", "Card"),
        new("receipt", "Bills"),
        new("savings", "Savings"),
        new("wallet", "Spending money"),
        new("star", "Treats"),
        new(Default, "Other"),
    ];

    public static bool IsKnown(string? key) => key is not null && All.Any(i => i.Key == key);

    /// <returns>The icon's name for screen readers, or "Other" for a key that isn't known.</returns>
    public static string LabelFor(string? key) => All.FirstOrDefault(i => i.Key == key)?.Label ?? "Other";
}
