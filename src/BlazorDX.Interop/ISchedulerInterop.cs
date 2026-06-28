namespace BlazorDX.Interop;

/// <summary>Which drag gesture the scheduler bridge observed.</summary>
public enum SchedulerDragKind
{
    /// <summary>An existing event was dragged to a new time slot.</summary>
    Move,

    /// <summary>An empty range was swept out to propose a new event.</summary>
    Create,
}

/// <summary>
/// One pointer-drag result reported by the scheduler time grid. All values are derived from the
/// pointer geometry on the JavaScript side and are <em>untrusted</em>: the host re-validates the
/// index against its own event list and clamps the day/hour into the visible range before acting.
/// </summary>
/// <param name="Kind">Move (drag an event) or Create (sweep an empty range).</param>
/// <param name="SourceIndex">For a move, the dragged event's row index (the value stamped on the block); -1 otherwise.</param>
/// <param name="DayIndex">Zero-based day column the gesture ended on.</param>
/// <param name="StartHour">Start of the slot in fractional hours from midnight (snapped on the JS side).</param>
/// <param name="EndHour">For a create, the swept end in fractional hours; unused for a move.</param>
public readonly record struct SchedulerDragResult(
    SchedulerDragKind Kind,
    int SourceIndex,
    int DayIndex,
    double StartHour,
    double EndHour);

/// <summary>
/// The thin native bridge that gives the scheduler time grid pointer-driven drag-to-move and
/// drag-to-create, plus the edge auto-scroll/measure those gestures need. All layout, date math,
/// and state stay in C#; this bridge only watches the pointer and reports a snapped result back.
/// The grid is addressed by element id so no <c>ElementReference</c> crosses the boundary (matching
/// <see cref="IFileDndInterop"/>). Outside WebAssembly it is a no-op (<see cref="NullSchedulerInterop"/>);
/// the keyboard active-cell model and click selection work without it.
/// </summary>
public interface ISchedulerInterop : IAsyncDisposable
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>
    /// Wires (or re-wires) pointer drag on the time-grid body with the given geometry. Idempotent:
    /// re-registering the same id replaces the prior wiring, so it is safe to call every render.
    /// <paramref name="onDrag"/> fires once per completed gesture with the snapped result.
    /// </summary>
    /// <param name="gridId">Element id of the time-grid body.</param>
    /// <param name="dayCount">Number of day columns.</param>
    /// <param name="startHour">First visible hour (maps to the top of a column).</param>
    /// <param name="endHour">Last visible hour (exclusive upper bound).</param>
    /// <param name="hourHeight">Pixel height of one hour row.</param>
    /// <param name="onDrag">Callback invoked with each completed drag/create result.</param>
    ValueTask RegisterTimeGridAsync(
        string gridId,
        int dayCount,
        int startHour,
        int endHour,
        int hourHeight,
        Action<SchedulerDragResult> onDrag);

    /// <summary>Tears down whatever was wired for the grid id (e.g. when switching to Month view).</summary>
    ValueTask UnregisterAsync(string gridId);
}
