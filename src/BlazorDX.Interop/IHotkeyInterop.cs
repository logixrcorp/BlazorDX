namespace BlazorDX.Interop;

/// <summary>
/// Browser bridge for global keyboard shortcuts: a single document keydown
/// listener that matches normalized combos and notifies .NET (preventing the
/// browser default for registered combos). Only functional under WebAssembly.
/// </summary>
public interface IHotkeyInterop
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>Attaches the listener and routes matched combos to <paramref name="onMatch"/>.</summary>
    ValueTask SubscribeAsync(Action<string> onMatch);

    /// <summary>Sets the combos to intercept (normalized, e.g. "ctrl+k").</summary>
    ValueTask SetBindingsAsync(string[] combos);

    /// <summary>Clears all bindings and the callback.</summary>
    ValueTask UnsubscribeAsync();
}
