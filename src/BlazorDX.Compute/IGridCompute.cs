namespace BlazorDX.Compute;

/// <summary>
/// Heavy grid operations (sort, filter) expressed as row-index transforms.
/// A column of values goes in; a permutation or subset of row indices comes out.
/// The active implementation may run in Rust/wasm or managed C#; callers neither
/// know nor care, which is what lets the grid work in every render mode.
/// </summary>
public interface IGridCompute
{
    /// <summary>Human-readable name of the active backend, e.g. for diagnostics UI.</summary>
    string Backend { get; }

    /// <summary>Returns a row-index permutation that orders <paramref name="values"/>.</summary>
    ValueTask<int[]> SortAsync(double[] values, bool descending);

    /// <summary>Returns the indices of rows whose value is at or above <paramref name="threshold"/>.</summary>
    ValueTask<int[]> FilterGreaterOrEqualAsync(double[] values, double threshold);

    /// <summary>Computes summary statistics (count/sum/min/max/mean) over a column.</summary>
    ValueTask<GridAggregate> AggregateAsync(double[] values);

    /// <summary>
    /// Bins <paramref name="values"/> into <paramref name="bins"/> equal-width
    /// buckets spanning [min, max], returning the count in each bucket.
    /// </summary>
    ValueTask<int[]> HistogramAsync(double[] values, int bins, double min, double max);

    /// <summary>
    /// LTTB-downsamples an (x, y) series to roughly <paramref name="threshold"/>
    /// points, returning the indices of the points to keep (shape preserved).
    /// </summary>
    ValueTask<int[]> DownsampleAsync(double[] x, double[] y, int threshold);
}
