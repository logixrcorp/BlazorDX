namespace BlazorDX.Interop;

/// <summary>
/// The browser-side compute bridge: runs the grid kernels in the Rust wasm
/// module. Implementations are only functional inside a WebAssembly runtime;
/// callers on the server use the managed fallback in BlazorDX.Compute instead.
/// </summary>
public interface IGridWasmInterop : IAsyncDisposable
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>Returns a row-index permutation that sorts <paramref name="values"/>.</summary>
    ValueTask<int[]> SortAsync(double[] values, bool descending);

    /// <summary>Returns the indices of rows whose value is at or above the threshold.</summary>
    ValueTask<int[]> FilterGreaterOrEqualAsync(double[] values, double threshold);

    /// <summary>Returns the column statistics as [count, sum, min, max, mean].</summary>
    ValueTask<double[]> AggregateAsync(double[] values);

    /// <summary>Returns per-bin counts for <paramref name="bins"/> buckets over [min, max].</summary>
    ValueTask<int[]> HistogramAsync(double[] values, int bins, double min, double max);

    /// <summary>LTTB-downsamples the (x, y) series, returning kept point indices.</summary>
    ValueTask<int[]> DownsampleAsync(double[] x, double[] y, int threshold);
}
