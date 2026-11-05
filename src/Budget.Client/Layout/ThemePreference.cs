using Microsoft.JSInterop;

namespace Budget.Client.Layout;

public enum ThemeMode
{
    /// <summary>Follow the device's light or dark setting.</summary>
    System,
    Light,
    Dark,
}

/// <summary>The person's choice of light, dark or system theme, remembered on this device (BUD-39).</summary>
public sealed class ThemePreference(IJSRuntime js)
{
    private const string StorageKey = "budget.theme";

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public event EventHandler? Changed;

    public async Task LoadAsync()
    {
        try
        {
            var stored = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (Enum.TryParse<ThemeMode>(stored, out var mode) && Enum.IsDefined(mode))
            {
                Mode = mode;
            }
        }
        catch (JSException)
        {
            // Storage can be blocked (e.g. private browsing); fall back to the system setting.
        }
    }

    public async Task SetAsync(ThemeMode mode)
    {
        Mode = mode;
        Changed?.Invoke(this, EventArgs.Empty);

        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, mode.ToString());
        }
        catch (JSException)
        {
            // Not remembered, but still applied for this visit.
        }
    }
}
