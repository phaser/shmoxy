using Microsoft.JSInterop;

namespace shmoxy.frontend.services;

/// <summary>
/// Manages global keyboard shortcuts via JS interop.
/// Components subscribe to shortcut events via OnShortcutTriggered.
/// </summary>
public sealed class KeyboardShortcutService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<KeyboardShortcutService>? _dotnetRef;
    private bool _initialized;

    public event Action<string>? OnShortcutTriggered;
    public event Action? OnResendRequested;

    public KeyboardShortcutService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _dotnetRef = DotNetObjectReference.Create(this);
        try
        {
            await _js.InvokeVoidAsync("keyboardShortcuts.init", _dotnetRef);
            _initialized = true;
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected
        }
    }

    [JSInvokable]
    public void OnShortcut(string action)
    {
        OnShortcutTriggered?.Invoke(action);
    }

    public void RequestResend()
    {
        OnResendRequested?.Invoke();
    }

    public async Task FocusSearchAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("keyboardShortcuts.focusElement", ".filters fluent-text-field input");
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_initialized)
        {
            try
            {
                await _js.InvokeVoidAsync("keyboardShortcuts.dispose");
            }
            catch (JSDisconnectedException)
            {
                // Circuit disconnected
            }
        }
        _dotnetRef?.Dispose();
    }
}
