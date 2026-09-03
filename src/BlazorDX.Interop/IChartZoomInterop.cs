namespace BlazorDX.Interop;

/// <summary>
/// The one DOM measurement a zoomable chart (line, area) needs: its SVG's actual rendered
/// CSS-pixel width. The chart's `Width` parameter drives its internal viewBox/point-projection
/// math, but <c>dx-chart.css</c> sets the SVG to <c>width: 100%</c>, so that parameter does not
/// reflect the real on-screen size once the page lays it out. Converting a drag-pan pixel delta
/// into a data-domain delta needs the real width — measured once per pan gesture (at
/// pointerdown), not per pointermove. Elements are addressed by id, same as
/// <see cref="IGridDomInterop"/>.
/// </summary>
public interface IChartZoomInterop : IAsyncDisposable
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>
    /// The element's rendered CSS-pixel client width, or 0 if the element can't be found (or
    /// there's no DOM at all — server/prerender). Callers must treat 0 as "measurement
    /// unavailable" and degrade gracefully rather than dividing by it directly.
    /// </summary>
    ValueTask<double> MeasureWidthAsync(string elementId);
}
