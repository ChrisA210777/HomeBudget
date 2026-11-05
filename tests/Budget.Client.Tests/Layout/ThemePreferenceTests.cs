using Budget.Client.Layout;
using Microsoft.Extensions.DependencyInjection;

namespace Budget.Client.Tests.Layout;

public sealed class ThemePreferenceTests : IDisposable
{
    private readonly TestContext _context = new();

    public ThemePreferenceTests() => _context.Services.AddScoped<ThemePreference>();

    [Theory]
    [InlineData("Dark", ThemeMode.Dark)]
    [InlineData("Light", ThemeMode.Light)]
    [InlineData("System", ThemeMode.System)]
    [InlineData(null, ThemeMode.System)]
    [InlineData("Sepia", ThemeMode.System)]
    [InlineData("7", ThemeMode.System)]
    public async Task Loads_the_choice_remembered_on_this_device(string? stored, ThemeMode expected)
    {
        _context.JSInterop.Setup<string?>("localStorage.getItem", "budget.theme").SetResult(stored);
        var preference = _context.Services.GetRequiredService<ThemePreference>();

        await preference.LoadAsync();

        Assert.Equal(expected, preference.Mode);
    }

    [Fact]
    public async Task Falls_back_to_the_device_setting_if_storage_is_blocked()
    {
        _context.JSInterop.Setup<string?>("localStorage.getItem", "budget.theme").SetException(new Microsoft.JSInterop.JSException("blocked"));
        var preference = _context.Services.GetRequiredService<ThemePreference>();

        await preference.LoadAsync();

        Assert.Equal(ThemeMode.System, preference.Mode);
    }

    public void Dispose() => _context.Dispose();
}
