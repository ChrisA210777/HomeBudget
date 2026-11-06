using Budget.Shared.Outgoings;
using MudBlazor;

namespace Budget.Client.Outgoings;

/// <summary>The Material icon for each <see cref="CategoryIcons"/> key (BUD-69).</summary>
public static class CategoryIconSvg
{
    private static readonly Dictionary<string, string> Svgs = new(StringComparer.Ordinal)
    {
        ["home"] = Icons.Material.Filled.Home,
        ["bolt"] = Icons.Material.Filled.Bolt,
        ["water"] = Icons.Material.Filled.WaterDrop,
        ["wifi"] = Icons.Material.Filled.Wifi,
        ["phone"] = Icons.Material.Filled.PhoneIphone,
        ["shield"] = Icons.Material.Filled.Shield,
        ["build"] = Icons.Material.Filled.Build,
        ["garden"] = Icons.Material.Filled.Yard,
        ["cleaning"] = Icons.Material.Filled.CleaningServices,
        ["car"] = Icons.Material.Filled.DirectionsCar,
        ["fuel"] = Icons.Material.Filled.LocalGasStation,
        ["train"] = Icons.Material.Filled.Train,
        ["flight"] = Icons.Material.Filled.Flight,
        ["holiday"] = Icons.Material.Filled.BeachAccess,
        ["pets"] = Icons.Material.Filled.Pets,
        ["health"] = Icons.Material.Filled.LocalHospital,
        ["care"] = Icons.Material.Filled.Spa,
        ["fitness"] = Icons.Material.Filled.FitnessCenter,
        ["cart"] = Icons.Material.Filled.ShoppingCart,
        ["restaurant"] = Icons.Material.Filled.Restaurant,
        ["coffee"] = Icons.Material.Filled.LocalCafe,
        ["child"] = Icons.Material.Filled.ChildCare,
        ["school"] = Icons.Material.Filled.School,
        ["tv"] = Icons.Material.Filled.Tv,
        ["subscriptions"] = Icons.Material.Filled.Subscriptions,
        ["music"] = Icons.Material.Filled.MusicNote,
        ["games"] = Icons.Material.Filled.SportsEsports,
        ["books"] = Icons.Material.Filled.MenuBook,
        ["computer"] = Icons.Material.Filled.Computer,
        ["ticket"] = Icons.Material.Filled.ConfirmationNumber,
        ["celebration"] = Icons.Material.Filled.Celebration,
        ["gift"] = Icons.Material.Filled.CardGiftcard,
        ["charity"] = Icons.Material.Filled.VolunteerActivism,
        ["bank"] = Icons.Material.Filled.AccountBalance,
        ["card"] = Icons.Material.Filled.CreditCard,
        ["receipt"] = Icons.Material.Filled.ReceiptLong,
        ["savings"] = Icons.Material.Filled.Savings,
        ["wallet"] = Icons.Material.Filled.AccountBalanceWallet,
        ["star"] = Icons.Material.Filled.Star,
        [CategoryIcons.Default] = Icons.Material.Filled.Label,
    };

    /// <returns>The icon's SVG; a plain label for a key that isn't known.</returns>
    public static string For(string? key) => key is not null && Svgs.TryGetValue(key, out var svg) ? svg : Icons.Material.Filled.Label;

    /// <summary>Every key in <see cref="CategoryIcons.All"/> has its own icon (checked by a test).</summary>
    public static bool Has(string key) => Svgs.ContainsKey(key);
}
