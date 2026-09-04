namespace BlazorDX.Primitives.Charts;

/// <summary>
/// Headless visible-domain zoom/pan state for a genuinely two-continuous-axis chart
/// (scatter, bubble — unlike line/area, which only ever zoom X). Composes two
/// independent <see cref="ChartZoomPrimitive"/> instances, one per axis, exposed
/// directly as <see cref="X"/>/<see cref="Y"/> so callers keep reading axis-specific
/// state (<c>VisibleMin</c>, <c>DataMax</c>, ...) through the same already-tested 1-D
/// API line/area charts already use — this type only adds the coordination a
/// rectangular gesture needs (a combined <see cref="IsZoomed"/>, and operations that
/// must apply to both axes together). See docs/adr/0020-scatter-bubble-2d-zoom-strategy.md.
/// </summary>
public sealed class ChartRectZoomPrimitive
{
    public ChartZoomPrimitive X { get; } = new();

    public ChartZoomPrimitive Y { get; } = new();

    /// <summary>True when either axis is zoomed in from its full domain.</summary>
    public bool IsZoomed => X.IsZoomed || Y.IsZoomed;

    public void SetDomain(double xMin, double xMax, double yMin, double yMax)
    {
        X.SetDomain(xMin, xMax);
        Y.SetDomain(yMin, yMax);
    }

    /// <summary>Zooms both axes by the same relative factor, each around its own
    /// current window's midpoint — the wheel-zoom case.</summary>
    public void ZoomIn(double factor = 1.35)
    {
        X.ZoomIn(factor);
        Y.ZoomIn(factor);
    }

    public void ZoomOut(double factor = 1.35)
    {
        X.ZoomOut(factor);
        Y.ZoomOut(factor);
    }

    /// <summary>Pans both axes by their own fraction of their own current visible span —
    /// the keyboard pan case (X and Y move independently, e.g. Shift+ArrowLeft pans only X).</summary>
    public void PanByFraction(double xFraction, double yFraction)
    {
        X.PanByFraction(xFraction);
        Y.PanByFraction(yFraction);
    }

    /// <summary>Jumps both axes directly to an arbitrary box — the brush-to-zoom gesture.</summary>
    public void ZoomToBox(double xMin, double xMax, double yMin, double yMax)
    {
        X.SetVisible(xMin, xMax);
        Y.SetVisible(yMin, yMax);
    }

    public void Reset()
    {
        X.Reset();
        Y.Reset();
    }
}
