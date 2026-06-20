using BlazorDX.Compute;
using Xunit;

namespace BlazorDX.Compute.Tests;

/// <summary>
/// Verifies the managed compute fallback. These results are the contract the Rust
/// kernels must also satisfy, so the grid behaves identically in every render mode.
/// </summary>
public sealed class ManagedGridComputeTests
{
    private readonly ManagedGridCompute compute = new();

    [Fact]
    public async Task Sort_returns_ascending_permutation()
    {
        int[] order = await compute.SortAsync([3.0, 1.0, 2.0], descending: false);
        Assert.Equal([1, 2, 0], order);
    }

    [Fact]
    public async Task Sort_returns_descending_permutation()
    {
        int[] order = await compute.SortAsync([3.0, 1.0, 2.0], descending: true);
        Assert.Equal([0, 2, 1], order);
    }

    [Fact]
    public async Task Filter_returns_indices_at_or_above_threshold()
    {
        int[] matched = await compute.FilterGreaterOrEqualAsync([10.0, 5.0, 20.0, 5.0], threshold: 10.0);
        Assert.Equal([0, 2], matched);
    }

    [Fact]
    public async Task Sort_permutation_covers_every_row_once()
    {
        double[] values = [5.0, 5.0, 1.0, 9.0, 1.0];
        int[] order = await compute.SortAsync(values, descending: false);
        Assert.Equal(Enumerable.Range(0, values.Length), order.OrderBy(i => i));
    }

    [Fact]
    public async Task Aggregate_computes_summary_statistics()
    {
        GridAggregate stats = await compute.AggregateAsync([2.0, 4.0, 6.0, 8.0]);
        Assert.Equal(4, stats.Count);
        Assert.Equal(20, stats.Sum);
        Assert.Equal(2, stats.Min);
        Assert.Equal(8, stats.Max);
        Assert.Equal(5, stats.Mean);
    }

    [Fact]
    public async Task Aggregate_of_empty_column_is_empty()
    {
        GridAggregate stats = await compute.AggregateAsync([]);
        Assert.Equal(0, stats.Count);
    }

    [Fact]
    public async Task Downsample_hits_threshold_and_keeps_endpoints()
    {
        double[] x = Enumerable.Range(0, 2000).Select(i => (double)i).ToArray();
        double[] y = x.Select(v => Math.Sin(v / 50)).ToArray();

        int[] kept = await compute.DownsampleAsync(x, y, 200);

        Assert.Equal(200, kept.Length);
        Assert.Equal(0, kept[0]);
        Assert.Equal(1999, kept[^1]);
        // Indices are strictly increasing.
        Assert.Equal(kept.OrderBy(i => i), kept);
    }

    [Fact]
    public async Task Downsample_returns_all_points_below_threshold()
    {
        double[] x = [0, 1, 2, 3];
        double[] y = [0, 1, 0, 1];

        int[] kept = await compute.DownsampleAsync(x, y, 100);

        Assert.Equal([0, 1, 2, 3], kept);
    }

    [Fact]
    public async Task Histogram_bins_values_and_includes_max_in_last_bin()
    {
        // 4 bins over [0,4]: [0,1)[1,2)[2,3)[3,4]; 4.0 lands in the last bin.
        int[] counts = await compute.HistogramAsync([0.0, 1.0, 1.0, 2.0, 3.0, 4.0], 4, 0.0, 4.0);
        Assert.Equal([1, 2, 1, 2], counts);
    }

    [Fact]
    public async Task Histogram_ignores_out_of_range_and_nan()
    {
        // -1 and 9 are out of range, NaN skipped; 0.5 sits on the bin boundary -> bin 1.
        int[] counts = await compute.HistogramAsync([-1.0, 0.5, double.NaN, 9.0], 2, 0.0, 1.0);
        Assert.Equal([0, 1], counts);
    }

    [Fact]
    public async Task Histogram_total_equals_in_range_count()
    {
        double[] values = Enumerable.Range(0, 1000).Select(i => i / 10.0).ToArray();
        int[] counts = await compute.HistogramAsync(values, 10, 0.0, 100.0);
        Assert.Equal(values.Length, counts.Sum());
    }

    [Fact]
    public void Backend_name_identifies_the_managed_path()
    {
        Assert.Equal("Managed C#", compute.Backend);
    }
}
