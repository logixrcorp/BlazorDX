namespace BlazorDX.Interop;

/// <summary>
/// Off-browser no-op hotkey bridge (static SSR / Interactive Server prerender),
/// where there is no document to listen on.
/// </summary>
public sealed class NullHotkeyInterop : IHotkeyInterop
{
    public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

    public ValueTask SubscribeAsync(Action<string> onMatch) => ValueTask.CompletedTask;

    public ValueTask SetBindingsAsync(string[] combos) => ValueTask.CompletedTask;

    public ValueTask UnsubscribeAsync() => ValueTask.CompletedTask;
}
