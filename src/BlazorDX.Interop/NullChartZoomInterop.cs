namespace BlazorDX.Interop;

/// <summary>
/// Server-side / non-browser implementation of <see cref="IChartZoomInterop"/>. There is no DOM
/// to measure outside WebAssembly, so it reports a width of 0 — a zoomable chart's drag-pan
/// gesture reads that as "measurement unavailable" and simply doesn't start for that gesture
/// (wheel-zoom and keyboard zoom/pan don't depend on this interop at all, so they still work).
/// </summary>
public sealed class NullChartZoomInterop : IChartZoomInterop
{
    public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

    public ValueTask<double> MeasureWidthAsync(string elementId) => ValueTask.FromResult(0.0);

    public ValueTask<(double Left, double Top)> MeasureOffsetAsync(string elementId) =>
        ValueTask.FromResult((0.0, 0.0));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
