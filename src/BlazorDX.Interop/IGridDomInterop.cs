namespace BlazorDX.Interop;

/// <summary>
/// The DOM operations the grid needs that WebAssembly cannot do itself: reading
/// a scroll container's viewport metrics (for virtualization) and moving focus.
/// Elements are addressed by id so no ElementReference crosses the interop boundary.
/// </summary>
public interface IGridDomInterop : IAsyncDisposable
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>Returns the element's scroll window as (scrollTop, clientHeight, scrollHeight).</summary>
    ValueTask<(double ScrollTop, double ClientHeight, double ScrollHeight)> MeasureViewportAsync(string elementId);

    /// <summary>Invokes <paramref name="onScroll"/> (throttled to a frame) whenever the element scrolls.</summary>
    ValueTask SubscribeScrollAsync(string elementId, Action onScroll);

    /// <summary>Moves focus to the first focusable descendant of the element.</summary>
    ValueTask FocusFirstAsync(string elementId);

    /// <summary>Triggers a client-side download of text content (e.g. an exported CSV).</summary>
    ValueTask DownloadTextAsync(string filename, string mime, string content);

    /// <summary>Triggers a client-side download of binary content (e.g. an exported .xlsx workbook).</summary>
    ValueTask DownloadBytesAsync(string filename, string mime, byte[] content);

    /// <summary>Writes text to the system clipboard; returns whether it succeeded.</summary>
    ValueTask<bool> WriteClipboardAsync(string text);

    /// <summary>Scrolls a container to an absolute vertical position (keyboard cell navigation).</summary>
    ValueTask ScrollToAsync(string elementId, double top);

    /// <summary>Suppresses native arrow/page scrolling within the grid, except inside text inputs.</summary>
    ValueTask SuppressArrowKeysAsync(string elementId);
}
