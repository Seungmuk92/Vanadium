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
