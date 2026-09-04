namespace BlazorDX.Primitives.Charts;

/// <summary>
/// Headless visible-domain state for a continuous chart's zoom/pan (line, area — the two chart
/// types <see cref="ChartSelectionPrimitive"/>'s doc comment names as "better served by a future
/// zoom/pan interaction, not per-point selection"). Tracks a [VisibleMin, VisibleMax] window
/// inside the full [DataMin, DataMax] domain. X-axis only: the visible Y range always covers the
/// full dataset regardless of zoom level — the standard "zoom a time series" model narrows the
/// X window while keeping Y stable so magnitude stays visually comparable across zoom levels.
/// Geometry-agnostic — the caller decides whether the domain is real X units (line chart) or
/// point-index units (area chart, which lays out by index).
/// </summary>
public sealed class ChartZoomPrimitive
{
    private const double MinSpanFraction = 0.02; // can't zoom in past 2% of the full domain

    public double DataMin { get; private set; }

    public double DataMax { get; private set; }

    public double VisibleMin { get; private set; }

    public double VisibleMax { get; private set; }

    public double VisibleSpan => VisibleMax - VisibleMin;

    private double DataSpan => DataMax - DataMin;

    /// <summary>
    /// True when the visible window is meaningfully narrower than the full domain. Compares with
    /// a small relative tolerance (not a strict <c>&gt;</c>/<c>&lt;</c>) — repeated zoom/pan
    /// arithmetic (e.g. a <see cref="ZoomIn"/> exactly undone by a <see cref="ZoomOut"/>) can land
    /// VisibleMin/VisibleMax a few ULPs off DataMin/DataMax rather than exactly on them; without
    /// the tolerance, that floating-point residue alone would keep this true (and a reset button
    /// visibly lingering) even though the view is, for every practical purpose, back to unzoomed.
    /// </summary>
    public bool IsZoomed
    {
        get
        {
            double epsilon = DataSpan * 1e-9;
            return VisibleMin > DataMin + epsilon || VisibleMax < DataMax - epsilon;
        }
    }

    /// <summary>
    /// (Re)anchors the domain when the dataset changes. If the domain's extent hasn't actually
    /// changed since the last call (the common no-op re-render case), the current zoom/pan window
    /// is preserved. If it genuinely changed (new data), the view resets to the full new domain —
    /// a stale window into old data would be actively misleading. A degenerate domain
    /// (<paramref name="min"/> == <paramref name="max"/>, e.g. a single-point series) collapses to
    /// a 1-unit span so downstream division-by-span math never sees zero.
    /// </summary>
    public void SetDomain(double min, double max)
    {
        if (max <= min)
        {
            max = min + 1;
        }

        if (min == DataMin && max == DataMax)
        {
            return;
        }

        DataMin = min;
        DataMax = max;
        VisibleMin = min;
        VisibleMax = max;
    }

    /// <summary>
    /// Zooms by <paramref name="factor"/> (&gt;1 in, &lt;1 out), keeping <paramref name="anchor"/>
    /// (a data-space value) fixed at the same fraction of the visible window. Clamped so the span
    /// never exceeds <see cref="DataSpan"/> (zoom out) or drops below <c>MinSpanFraction *
    /// DataSpan</c> (zoom in).
    /// </summary>
    public void ZoomAt(double anchor, double factor)
    {
        if (factor <= 0 || DataSpan <= 0)
        {
            return;
        }

        double newSpan = Math.Clamp(VisibleSpan / factor, MinSpanFraction * DataSpan, DataSpan);

        // Keep `anchor`'s fraction through the window constant: anchor = min + fraction * span.
        double fraction = VisibleSpan > 0 ? (anchor - VisibleMin) / VisibleSpan : 0.5;
        double newMin = anchor - (fraction * newSpan);
        double newMax = newMin + newSpan;

        if (newMin < DataMin)
        {
            newMin = DataMin;
            newMax = newMin + newSpan;
        }
        else if (newMax > DataMax)
        {
            newMax = DataMax;
            newMin = newMax - newSpan;
        }

        VisibleMin = newMin;
        VisibleMax = newMax;
    }

    /// <summary>Zooms around the current visible window's midpoint — the keyboard/wheel case, where
    /// there's no cursor position (or it's deliberately not used) to anchor to.</summary>
    public void ZoomIn(double factor = 1.35) => ZoomAt((VisibleMin + VisibleMax) / 2, factor);

    /// <summary>Zooms out around the current visible window's midpoint.</summary>
    public void ZoomOut(double factor = 1.35) => ZoomAt((VisibleMin + VisibleMax) / 2, 1 / factor);

    /// <summary>Shifts the visible window by <paramref name="delta"/> data-space units, clamped so
    /// it can't pan past <see cref="DataMin"/>/<see cref="DataMax"/>. Span is unchanged.</summary>
    public void PanBy(double delta)
    {
        double newMin = VisibleMin + delta;
        double newMax = VisibleMax + delta;

        if (newMin < DataMin)
        {
            newMax += DataMin - newMin;
            newMin = DataMin;
        }
        else if (newMax > DataMax)
        {
            newMin -= newMax - DataMax;
            newMax = DataMax;
        }

        VisibleMin = newMin;
        VisibleMax = newMax;
    }

    /// <summary>Convenience for keyboard arrow-key panning: shifts by <paramref name="fraction"/>
    /// of the current visible span (e.g. 0.1 = 10% per key press) — same clamping as
    /// <see cref="PanBy"/>. A negative fraction pans toward <see cref="DataMin"/>.</summary>
    public void PanByFraction(double fraction) => PanBy(fraction * VisibleSpan);

    /// <summary>Restores the full domain. Also what <see cref="SetDomain"/> does on a genuine
    /// data change.</summary>
    public void Reset()
    {
        VisibleMin = DataMin;
        VisibleMax = DataMax;
    }

    /// <summary>
    /// Directly sets the visible window to [<paramref name="min"/>, <paramref name="max"/>],
    /// clamped to the domain and to <c>MinSpanFraction</c> — the one operation none of
    /// <see cref="ZoomAt"/>/<see cref="PanBy"/>/<see cref="Reset"/> provide (they're all relative
    /// to the current window). Needed by a rectangle-drag-to-zoom gesture, which jumps to an
    /// arbitrary box rather than adjusting the existing one.
    /// </summary>
    public void SetVisible(double min, double max)
    {
        if (DataSpan <= 0)
        {
            return;
        }

        if (max < min)
        {
            (min, max) = (max, min);
        }

        // Clip to the domain — deliberately NOT PanBy's shift-to-preserve-span behavior: a
        // brush that ran past the edge should stop at the edge, not slide the window inward
        // to keep the (over-large) span it was dragged with.
        min = Math.Max(DataMin, min);
        max = Math.Min(DataMax, max);

        // Only if the clipped box is too small does the span get expanded — around its own
        // midpoint, then pushed back inside the domain if that expansion crossed an edge.
        double minSpan = MinSpanFraction * DataSpan;
        if (max - min < minSpan)
        {
            double mid = (min + max) / 2;
            min = mid - (minSpan / 2);
            max = mid + (minSpan / 2);

            if (min < DataMin)
            {
                min = DataMin;
                max = DataMin + minSpan;
            }
            else if (max > DataMax)
            {
                max = DataMax;
                min = DataMax - minSpan;
            }
        }

        VisibleMin = min;
        VisibleMax = max;
    }
}
