namespace BlazorDX.Interop;

/// <summary>
/// Positions a floating element (popover, tooltip, dropdown) relative to an anchor,
/// with collision flip and viewport shift, and keeps it positioned while scrolling
/// or resizing. Elements are addressed by id. Outside WebAssembly this is a no-op.
/// </summary>
public interface IAnchorInterop : IAsyncDisposable
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>
    /// Positions <paramref name="floatingId"/> against <paramref name="anchorId"/> and
    /// tracks it until <see cref="DetachAsync"/>. <paramref name="side"/> is one of
    /// top/bottom/left/right; <paramref name="align"/> is start/center/end.
    /// </summary>
    ValueTask AttachAsync(string floatingId, string anchorId, string side, string align, int offset);

    /// <summary>Stops positioning the floating element and removes its listeners.</summary>
    ValueTask DetachAsync(string floatingId);
}
