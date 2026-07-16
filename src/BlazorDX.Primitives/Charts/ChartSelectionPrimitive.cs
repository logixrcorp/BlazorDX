namespace BlazorDX.Primitives.Charts;

/// <summary>
/// Headless roving-index selection/focus state for a discrete-mark chart (one bar, slice, radar
/// series, candle, or scatter dot per index). Geometry-agnostic by design: it only knows a point
/// count, not layout — the same state machine serves a categorical bar chart, an angular pie
/// chart, and a polar radar chart alike, mirroring the WAI-ARIA roving-tabindex pattern already
/// used for the DataGrid, Scheduler, and Calendar active-cell models. Continuous/downsampled
/// charts (line, area) and the decorative sparkline are deliberately out of scope — thousands of
/// LTTB-selected points aren't discrete, keyboard-tabbable marks the way a bar or slice is; those
/// chart types are better served by a future zoom/pan interaction, not per-point selection.
/// </summary>
public sealed class ChartSelectionPrimitive
{
    /// <summary>The keyboard-focused point's index, or -1 when nothing has been focused yet.</summary>
    public int ActiveIndex { get; private set; } = -1;

    /// <summary>The pointer-hovered point's index, or -1 when nothing is hovered.</summary>
    public int HoveredIndex { get; private set; } = -1;

    public bool HasActive => ActiveIndex >= 0;

    public bool IsActive(int index) => index == ActiveIndex;

    public bool IsHovered(int index) => index == HoveredIndex;

    /// <summary>Sets the hovered point (or clears it with -1). Pure state — the caller re-renders.</summary>
    public void SetHovered(int index) => HoveredIndex = index;

    /// <summary>Makes <paramref name="index"/> the active (focused) point directly, e.g. on click.</summary>
    public void SetActive(int index, int count)
    {
        if (index >= 0 && index < count)
        {
            ActiveIndex = index;
        }
    }

    /// <summary>Re-anchors the active index into range after the point count changes (e.g. new data).</summary>
    public void ClampTo(int count)
    {
        if (count <= 0)
        {
            ActiveIndex = -1;
            return;
        }

        if (HasActive)
        {
            ActiveIndex = Math.Clamp(ActiveIndex, 0, count - 1);
        }
    }

    /// <summary>
    /// Applies an arrow / Home / End key to the active index. Returns true when the key was a
    /// navigation key (so the caller can re-render and prevent default); false for any other key
    /// (including Enter/Space, which the caller handles separately via <see cref="HasActive"/>).
    /// The first navigation key seeds the active index at 0, matching the Scheduler/Calendar
    /// pattern. No wrap at the ends.
    /// </summary>
    public bool MoveActive(string key, int count)
    {
        if (count <= 0)
        {
            return false;
        }

        if (!HasActive)
        {
            if (key is "ArrowRight" or "ArrowLeft" or "ArrowUp" or "ArrowDown" or "Home" or "End")
            {
                ActiveIndex = 0;
                return true;
            }

            return false;
        }

        switch (key)
        {
            // Up/Down are accepted as synonyms so a vertical bar chart (categories run
            // left-to-right but bars grow upward) and a horizontal one both feel natural.
            case "ArrowRight" or "ArrowDown":
                ActiveIndex = Math.Min(count - 1, ActiveIndex + 1);
                break;
            case "ArrowLeft" or "ArrowUp":
                ActiveIndex = Math.Max(0, ActiveIndex - 1);
                break;
            case "Home":
                ActiveIndex = 0;
                break;
            case "End":
                ActiveIndex = count - 1;
                break;
            default:
                return false;
        }

        return true;
    }
}
