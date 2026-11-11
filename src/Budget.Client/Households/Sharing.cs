using Microsoft.JSInterop;

namespace Budget.Client.Households;

/// <summary>The browser's share sheet and clipboard, for sharing invitation links (ADR-015).</summary>
public interface ISharing
{
    Task<bool> CanShareAsync();

    Task<bool> ShareAsync(string title, string text, string url);

    Task<bool> CopyAsync(string text);
}

public sealed class Sharing(IJSRuntime js) : ISharing, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task<bool> CanShareAsync() => await (await ModuleAsync()).InvokeAsync<bool>("canShare");

    public async Task<bool> ShareAsync(string title, string text, string url) =>
        await (await ModuleAsync()).InvokeAsync<bool>("share", title, text, url);

    public async Task<bool> CopyAsync(string text) => await (await ModuleAsync()).InvokeAsync<bool>("copy", text);

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
    }

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/share.js");
}
