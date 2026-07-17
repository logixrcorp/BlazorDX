namespace BlazorDX.Primitives.Charts;

/// <summary>The five-number summary plus outliers for one box (Tukey's boxplot convention).</summary>
public readonly record struct BoxPlotStats(double Min, double Q1, double Median, double Q3, double Max, IReadOnlyList<double> Outliers);

/// <summary>
/// Quartile/outlier computation for <c>DxBoxPlot</c>. Pure math over an already-sorted sample —
/// sorting itself is the only genuinely heavy step for a large sample, and that's already offloaded
/// via <see cref="BlazorDX.Compute.IGridCompute.SortAsync"/>; this class does not duplicate that.
/// </summary>
public static class BoxPlotStatistics
{
    /// <param name="sortedValues">Ascending-sorted samples (NaN-free — filter before calling).</param>
    public static BoxPlotStats Compute(IReadOnlyList<double> sortedValues)
    {
        if (sortedValues.Count == 0)
        {
            return new BoxPlotStats(0, 0, 0, 0, 0, []);
        }

        double q1 = Percentile(sortedValues, 0.25);
        double median = Percentile(sortedValues, 0.5);
        double q3 = Percentile(sortedValues, 0.75);
        double iqr = q3 - q1;
        double lowFence = q1 - (1.5 * iqr);
        double highFence = q3 + (1.5 * iqr);

        List<double> outliers = [];
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (double v in sortedValues)
        {
            if (v < lowFence || v > highFence)
            {
                outliers.Add(v);
                continue;
            }

            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }

        // Every value was flagged an outlier (a degenerate/tiny sample) — the whiskers fall back
        // to the quartiles themselves rather than staying at +/-infinity.
        if (double.IsPositiveInfinity(min))
        {
            min = q1;
        }

        if (double.IsNegativeInfinity(max))
        {
            max = q3;
        }

        return new BoxPlotStats(min, q1, median, q3, max, outliers);
    }

    // Linear-interpolation percentile ("R-7" / Excel PERCENTILE.INC / numpy's default) — the most
    // commonly expected convention for a boxplot.
    private static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 1)
        {
            return sorted[0];
        }

        double rank = p * (sorted.Count - 1);
        int lo = (int)Math.Floor(rank);
        int hi = (int)Math.Ceiling(rank);
        if (lo == hi)
        {
            return sorted[lo];
        }

        double frac = rank - lo;
        return sorted[lo] + ((sorted[hi] - sorted[lo]) * frac);
    }
}
