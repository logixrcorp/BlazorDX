namespace BlazorDX.Compute;

/// <summary>Which summary statistic to display for an aggregated column.</summary>
public enum GridAggregateKind
{
    Sum,
    Mean,
    Min,
    Max,
    Count,
}

/// <summary>
/// Summary statistics for one column, computed in a single pass (by the Rust
/// kernel in the browser, or managed C# elsewhere).
/// </summary>
public readonly record struct GridAggregate(double Count, double Sum, double Min, double Max, double Mean)
{
    /// <summary>An empty result (zero count, NaN statistics).</summary>
    public static readonly GridAggregate Empty = new(0, 0, double.NaN, double.NaN, double.NaN);

    /// <summary>Returns the value for the requested statistic.</summary>
    public double Value(GridAggregateKind kind) => kind switch
    {
        GridAggregateKind.Sum => Sum,
        GridAggregateKind.Mean => Mean,
        GridAggregateKind.Min => Min,
        GridAggregateKind.Max => Max,
        GridAggregateKind.Count => Count,
        _ => double.NaN,
    };
}
