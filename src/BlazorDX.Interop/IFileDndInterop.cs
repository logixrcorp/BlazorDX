namespace BlazorDX.Interop;

/// <summary>One OS file reported by a native drag-and-drop drop (name/size/type only).</summary>
/// <remarks>
/// The drop reports file <em>metadata</em> across the interop boundary, not bytes:
/// actually streaming a dropped file is done through the framework's
/// <c>InputFile</c>/<c>IBrowserFile</c> path, which the file manager always offers
/// alongside drop. Native DnD is an enhancement, never the only upload route.
/// </remarks>
public readonly record struct DroppedFile(string Name, long Size, string ContentType);

/// <summary>
/// Native HTML5 drag-and-drop for the file manager: making items draggable, wiring
/// drop targets that move items within/between panes, and surfacing OS files dropped
/// onto a pane (the File API). Elements are addressed by id so no ElementReference
/// crosses the boundary (matching <see cref="IOverlayInterop"/>). Outside WebAssembly
/// these are no-ops (the <see cref="NullFileDndInterop"/> implementation is used).
/// </summary>
public interface IFileDndInterop : IAsyncDisposable
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>Marks the element draggable and stamps its id into the DataTransfer on dragstart.</summary>
    ValueTask RegisterDraggableAsync(string elementId);

    /// <summary>
    /// Wires a drop target. <paramref name="onMove"/> fires with the dragged item id
    /// and this target's id for an internal item move; <paramref name="onFiles"/> fires
    /// with the dropped OS files when files are dropped instead.
    /// </summary>
    ValueTask RegisterDropTargetAsync(
        string elementId,
        Action<string, string> onMove,
        Action<IReadOnlyList<DroppedFile>> onFiles);

    /// <summary>Tears down whatever was registered for the element id.</summary>
    ValueTask UnregisterAsync(string elementId);
}
