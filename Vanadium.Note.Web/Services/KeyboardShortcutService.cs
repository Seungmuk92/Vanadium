using Microsoft.JSInterop;

namespace Vanadium.Note.Web.Services;

public sealed class KeyboardShortcutService : IAsyncDisposable
{
    public record ShortcutEntry(string Key, string Description);

    private record HandlerEntry(Func<Task> Handler, string Description);

    private readonly IJSRuntime _js;
    private readonly Dictionary<string, HandlerEntry> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private DotNetObjectReference<KeyboardShortcutService>? _objRef;

    public KeyboardShortcutService(IJSRuntime js) => _js = js;

    public IReadOnlyList<ShortcutEntry> RegisteredShortcuts =>
        _handlers.Select(kv => new ShortcutEntry(kv.Key, kv.Value.Description))
                 .OrderBy(e => e.Key)
                 .ToList();

    public async Task InitAsync()
    {
        _objRef ??= DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("keyboardShortcuts.init", _objRef);
    }

    public async Task RegisterAsync(string key, string description, Func<Task> handler)
    {
        _handlers[key] = new HandlerEntry(handler, description);
        await _js.InvokeVoidAsync("keyboardShortcuts.register", key);
    }

    public async Task UnregisterAsync(string key)
    {
        _handlers.Remove(key);
        await _js.InvokeVoidAsync("keyboardShortcuts.unregister", key);
    }

    public async Task UnregisterManyAsync(params string[] keys)
    {
        foreach (var key in keys)
            await UnregisterAsync(key);
    }

    /// <summary>
    /// Temporarily takes over <paramref name="key"/> with a new handler and returns a token that,
    /// when disposed, restores the previously registered handler (or clears the binding if there
    /// was none). Modal dialogs use this to claim Ctrl+S for their own note while open and hand it
    /// back to the underlying page on close (#168).
    /// </summary>
    public async Task<IAsyncDisposable> OverrideAsync(string key, string description, Func<Task> handler)
    {
        _handlers.TryGetValue(key, out var previous);
        await RegisterAsync(key, description, handler);
        return new ShortcutOverride(this, key, previous);
    }

    private sealed class ShortcutOverride(KeyboardShortcutService owner, string key, HandlerEntry? previous)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (previous is not null)
            {
                owner._handlers[key] = previous;
                await owner._js.InvokeVoidAsync("keyboardShortcuts.register", key);
            }
            else
            {
                await owner.UnregisterAsync(key);
            }
        }
    }

    [JSInvokable]
    public async Task HandleShortcut(string key)
    {
        if (_handlers.TryGetValue(key, out var entry))
            await entry.Handler();
    }

    public async ValueTask DisposeAsync()
    {
        await _js.InvokeVoidAsync("keyboardShortcuts.dispose");
        _objRef?.Dispose();
    }
}
