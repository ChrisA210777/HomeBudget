using MudBlazor;

namespace Budget.Client.Layout;

/// <summary>
/// The app's colours in light and dark mode (BUD-39). Text and controls meet WCAG AA contrast
/// against the backgrounds they're used on in both modes.
/// </summary>
public static class AppTheme
{
    /// <summary>Also the PWA theme colour in manifest.webmanifest and index.html.</summary>
    public const string Brand = "#00695C";

    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = Brand,
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#546E7A",
            SecondaryContrastText = "#FFFFFF",
            Info = "#1565C0",
            InfoContrastText = "#FFFFFF",
            Success = "#2E7D32",
            SuccessContrastText = "#FFFFFF",
            Warning = "#B45309",
            WarningContrastText = "#FFFFFF",
            Error = "#C62828",
            ErrorContrastText = "#FFFFFF",
            AppbarBackground = Brand,
            AppbarText = "#FFFFFF",
            Background = "#F7F9F9",
            Surface = "#FFFFFF",
            DrawerBackground = "#FFFFFF",
            DrawerText = "rgba(0,0,0,0.87)",
            DrawerIcon = "rgba(0,0,0,0.64)",
            TextPrimary = "rgba(0,0,0,0.87)",
            TextSecondary = "rgba(0,0,0,0.64)",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#4DB6AC",
            PrimaryContrastText = "rgba(0,0,0,0.87)",
            Secondary = "#B0BEC5",
            SecondaryContrastText = "rgba(0,0,0,0.87)",
            Info = "#90CAF9",
            InfoContrastText = "rgba(0,0,0,0.87)",
            Success = "#81C784",
            SuccessContrastText = "rgba(0,0,0,0.87)",
            Warning = "#FFB74D",
            WarningContrastText = "rgba(0,0,0,0.87)",
            Error = "#EF9A9A",
            ErrorContrastText = "rgba(0,0,0,0.87)",
            AppbarBackground = "#1E1E1E",
            AppbarText = "rgba(255,255,255,0.92)",
            Background = "#121212",
            Surface = "#1E1E1E",
            DrawerBackground = "#1E1E1E",
            DrawerText = "rgba(255,255,255,0.87)",
            DrawerIcon = "rgba(255,255,255,0.74)",
            TextPrimary = "rgba(255,255,255,0.87)",
            TextSecondary = "rgba(255,255,255,0.72)",
            ActionDefault = "rgba(255,255,255,0.74)",
            LinesDefault = "rgba(255,255,255,0.16)",
        },
    };
}
