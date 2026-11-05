using Microsoft.JSInterop;

namespace Budget.Client.Layout;

/// <summary>
/// Whether the device is online, and whether a new version of the app is ready (BUD-38).
/// Fed by js/pwa.js.
/// </summary>
public sealed class Connectivity(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<Connectivity>? _self;

    public bool IsOnline { get; private set; } = true;

    public bool UpdateAvailable { get; private set; }

    /// <summary>Raised when <see cref="IsOnline"/> or <see cref="UpdateAvailable"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the connection comes back after being lost.</summary>
    public event EventHandler? Reconnected;

    public async Task StartAsync()
    {
        if (_module is not null)
        {
            return;
        }

        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/pwa.js");
            _self = DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("start", _self);
        }
        catch (JSException)
        {
            // Without the script the app still works; it just can't show these banners.
        }
    }

    public async Task ApplyUpdateAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("applyUpdate");
        }
    }

    [JSInvokable]
    public void OnConnectivityChanged(bool online)
    {
        if (online == IsOnline)
        {
            return;
        }

        IsOnline = online;
        Changed?.Invoke(this, EventArgs.Empty);
        if (online)
        {
            Reconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    [JSInvokable]
    public void OnUpdateAvailable()
    {
        UpdateAvailable = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        _self?.Dispose();
    }
}
