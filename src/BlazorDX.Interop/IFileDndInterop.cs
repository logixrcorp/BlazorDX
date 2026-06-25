namespace BlazorDX.Interop;

/// <summary>One OS file reported by a native drag-and-drop drop (name/size/type only).</summary>
/// <remarks>
/// The drop reports file <em>metadata</em> across the interop boundary, not bytes:
/// actually streaming a dropped file is done through the framework's
/// <c>InputFile</c>/<c>IBrowserFile</c> path, which the file manager always offers
/// alongside drop. Native DnD is an enhancement, never the only upload route.
/// <para><strong>Security:</strong> <see cref="Name"/> and <see cref="ContentType"/>
/// are <em>browser-reported</em> values taken from the dropped <c>File</c> objects and
/// must not be trusted by the host. Do not use <see cref="Name"/> as a path component
/// without re-validating it, and do not rely on <see cref="ContentType"/> for any
/// security decision (sniff the bytes instead). Names with path separators, null
/// bytes, or empty names are stripped at the interop boundary before they reach a
/// callback, but the surviving values are still untrusted.</para>
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

    /// <summary>
    /// Moves keyboard focus to the element with the given id (no-op if it is not in
    /// the DOM). Used to re-home focus after a move or upload so keyboard and
    /// screen-reader users are not stranded (WCAG 2.4.3).
    /// </summary>
    ValueTask FocusElementAsync(string elementId);
}
