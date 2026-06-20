namespace BlazorDX.Compute;

/// <summary>
/// Pure C# implementation of the grid kernels. It mirrors the Rust module so the
/// grid behaves identically on the server (static SSR, Interactive Server) and in
/// unit tests, where no wasm runtime is available.
/// </summary>
public sealed class ManagedGridCompute : IGridCompute
{
    public string Backend => "Managed C#";

    public ValueTask<int[]> SortAsync(double[] values, bool descending)
    {
        int[] order = new int[values.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (left, right) =>
        {
            int comparison = values[left].CompareTo(values[right]);
            return descending ? -comparison : comparison;
        });

        return ValueTask.FromResult(order);
    }

    public ValueTask<int[]> FilterGreaterOrEqualAsync(double[] values, double threshold)
    {
        List<int> matched = new();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] >= threshold)
            {
                matched.Add(i);
            }
        }

        return ValueTask.FromResult(matched.ToArray());
    }

    public ValueTask<GridAggregate> AggregateAsync(double[] values)
    {
        long count = 0;
        double sum = 0;
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;

        foreach (double value in values)
        {
            if (double.IsNaN(value))
            {
                continue;
            }

            count++;
            sum += value;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        GridAggregate result = count == 0
            ? GridAggregate.Empty
            : new GridAggregate(count, sum, min, max, sum / count);

        return ValueTask.FromResult(result);
    }

    public ValueTask<int[]> HistogramAsync(double[] values, int bins, double min, double max)
    {
        int[] counts = new int[Math.Max(0, bins)];
        double span = max - min;
        if (bins <= 0 || span <= 0)
        {
            return ValueTask.FromResult(counts);
        }

        int last = bins - 1;
        foreach (double value in values)
        {
            if (double.IsNaN(value) || value < min || value > max)
            {
                continue;
            }

            int bin = (int)((value - min) / span * bins);
            if (bin > last)
            {
                bin = last;   // value == max
            }

            counts[bin]++;
        }

        return ValueTask.FromResult(counts);
    }

    public ValueTask<int[]> DownsampleAsync(double[] x, double[] y, int threshold)
    {
        int n = x.Length;
        if (threshold >= n || threshold < 3)
        {
            return ValueTask.FromResult(Enumerable.Range(0, n).ToArray());
        }

        List<int> sampled = new(threshold) { 0 };
        double bucketSize = (double)(n - 2) / (threshold - 2);
        int anchor = 0;

        for (int i = 0; i < threshold - 2; i++)
        {
            int avgStart = (int)((i + 1) * bucketSize) + 1;
            int avgEnd = Math.Min((int)((i + 2) * bucketSize) + 1, n);
            int avgCount = Math.Max(1, avgEnd - avgStart);
            double avgX = 0;
            double avgY = 0;
            for (int j = avgStart; j < avgEnd; j++)
            {
                avgX += x[j];
                avgY += y[j];
            }

            avgX /= avgCount;
            avgY /= avgCount;

            int rangeStart = (int)(i * bucketSize) + 1;
            int rangeEnd = (int)((i + 1) * bucketSize) + 1;
            double anchorX = x[anchor];
            double anchorY = y[anchor];
            double maxArea = -1;
            int nextAnchor = rangeStart;
            for (int j = rangeStart; j < rangeEnd; j++)
            {
                double area = Math.Abs(
                    ((anchorX - avgX) * (y[j] - anchorY)) - ((anchorX - x[j]) * (avgY - anchorY))) * 0.5;
                if (area > maxArea)
                {
                    maxArea = area;
                    nextAnchor = j;
                }
            }

            sampled.Add(nextAnchor);
            anchor = nextAnchor;
        }

        sampled.Add(n - 1);
        return ValueTask.FromResult(sampled.ToArray());
    }
}
