namespace BlazorDX.Interop;

/// <summary>
/// Non-browser implementation of <see cref="IOverlayInterop"/>. Overlays still open
/// and close as Blazor state, but without DOM-level focus trapping, scroll locking,
/// or click-outside detection (there is no DOM to wire them to off-browser).
/// </summary>
public sealed class NullOverlayInterop : IOverlayInterop
{
    public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

    public ValueTask OpenAsync(
        string elementId,
        string ignoreElementId,
        bool trapFocus,
        bool lockScroll,
        bool closeOnEscape,
        bool closeOnOutsideClick,
        Action onDismiss) => ValueTask.CompletedTask;

    public ValueTask CloseAsync(string elementId) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
