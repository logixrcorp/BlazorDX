namespace BlazorDX.Interop;

/// <summary>
/// Shared DOM behaviors for overlays (Dialog, Sheet, Popover, ...): focus
/// trapping, scroll locking, and dismissal via Escape or click-outside. Overlays
/// are addressed by element id so no ElementReference crosses the boundary.
/// Outside WebAssembly these are no-ops (the no-op implementation is used).
/// </summary>
public interface IOverlayInterop : IAsyncDisposable
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>
    /// Activates the overlay's behaviors. <paramref name="onDismiss"/> is invoked
    /// when the user presses Escape or clicks outside (subject to the flags). Clicks
    /// inside <paramref name="ignoreElementId"/> (e.g. the trigger/anchor) do not
    /// count as outside; pass an empty string when there is none.
    /// </summary>
    ValueTask OpenAsync(
        string elementId,
        string ignoreElementId,
        bool trapFocus,
        bool lockScroll,
        bool closeOnEscape,
        bool closeOnOutsideClick,
        Action onDismiss);

    /// <summary>Tears down the overlay's behaviors (focus trap, scroll lock, listeners).</summary>
    ValueTask CloseAsync(string elementId);
}
