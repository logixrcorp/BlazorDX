using System.Globalization;
using BlazorDX.Compute;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A box plot: one box-and-whisker per <see cref="BoxPlotGroup"/> — Q1/median/Q3 box, whiskers to
/// the nearest non-outlier min/max, and outlier dots beyond 1.5x IQR (Tukey's convention,
/// <see cref="BoxPlotStatistics"/>). Set <see cref="Violin"/> to also draw a density silhouette
/// behind each box, binned via the same compute backend as <see cref="DxHistogram"/> (Rust in the
/// browser) over the shared value axis so every group's silhouette aligns. Pure SVG; styling via
/// dx-chart.css.
/// </summary>
public sealed class DxBoxPlot : ComponentBase
{
    private const int ViolinBins = 14;

    private IReadOnlyList<BoxPlotStats> stats = [];
    private IReadOnlyList<int[]> violinBins = [];
    private double axisMin;
    private double axisMax = 1;
    private object? lastGroups;

    [Parameter, EditorRequired] public IReadOnlyList<BoxPlotGroup> Groups { get; set; } = [];

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 300;

    /// <summary>Also draw a density silhouette (binned like <see cref="DxHistogram"/>) behind each box.</summary>
    [Parameter] public bool Violin { get; set; }

    [Parameter] public string? Class { get; set; }

    [Inject] private IGridCompute Compute { get; set; } = default!;

    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    protected override async Task OnParametersSetAsync()
    {
        if (ReferenceEquals(lastGroups, Groups))
        {
            return;
        }

        lastGroups = Groups;

        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (BoxPlotGroup g in Groups)
        {
            foreach (double v in g.Values)
            {
                if (double.IsNaN(v))
                {
                    continue;
                }

                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
        }

        if (double.IsPositiveInfinity(min))
        {
            min = 0;
            max = 1;
        }
        else if (min == max)
        {
            max = min + 1;   // guard a degenerate zero-span axis
        }

        axisMin = min;
        axisMax = max;

        BoxPlotStats[] computedStats = new BoxPlotStats[Groups.Count];
        int[][] computedBins = new int[Groups.Count][];
        for (int i = 0; i < Groups.Count; i++)
        {
            double[] values = Groups[i].Values.Where(v => !double.IsNaN(v)).ToArray();
            if (values.Length == 0)
            {
                computedStats[i] = new BoxPlotStats(0, 0, 0, 0, 0, []);
                computedBins[i] = [];
                continue;
            }

            int[] order = await Compute.SortAsync(values, descending: false);
            double[] sorted = order.Select(idx => values[idx]).ToArray();
            computedStats[i] = BoxPlotStatistics.Compute(sorted);
            computedBins[i] = Violin ? await Compute.HistogramAsync(values, ViolinBins, axisMin, axisMax) : [];
        }

        stats = computedStats;
        violinBins = computedBins;
        StateHasChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", "img");
        builder.AddAttribute(6, "aria-label", $"Box plot with {Groups.Count} groups");

        const double labelSpace = 22;
        double axisH = Height - labelSpace;
        double span = Math.Max(1e-9, axisMax - axisMin);
        double Y(double v) => axisH - ((v - axisMin) / span * (axisH - 10));

        double slot = Groups.Count == 0 ? Width : (double)Width / Groups.Count;
        double boxWidth = Math.Max(8, slot * 0.35);
        double maxViolinWidth = Math.Max(boxWidth, slot * 0.7);

        for (int i = 0; i < Groups.Count; i++)
        {
            BoxPlotGroup group = Groups[i];
            Text(builder, (i * slot) + (slot / 2), Height - 4, group.Label);

            if (group.Values.Count == 0 || stats.Count <= i)
            {
                continue;   // no data, or the async compute pass hasn't landed yet
            }

            double cx = (i * slot) + (slot / 2);
            string color = group.Color ?? Palette[i % Palette.Length];
            BoxPlotStats s = stats[i];

            if (Violin && violinBins.Count > i && violinBins[i].Length > 0)
            {
                BuildViolin(builder, i, violinBins[i], cx, Y, maxViolinWidth, axisMin, axisMax, color);
            }

            Whisker(builder, cx, Y(s.Min), Y(s.Q1), boxWidth * 0.5);
            Whisker(builder, cx, Y(s.Q3), Y(s.Max), boxWidth * 0.5);

            builder.OpenElement(30, "rect");
            builder.SetKey($"box{i}");
            builder.AddAttribute(31, "class", "dx-boxplot-box dx-chart-drawin");
            builder.AddAttribute(32, "x", F(cx - (boxWidth / 2)));
            builder.AddAttribute(33, "y", F(Y(s.Q3)));
            builder.AddAttribute(34, "width", F(boxWidth));
            builder.AddAttribute(35, "height", F(Math.Max(1, Y(s.Q1) - Y(s.Q3))));
            builder.AddAttribute(36, "stroke", color);
            builder.AddAttribute(37, "style", $"animation-delay:{i * 30}ms");
            builder.OpenElement(38, "title");
            builder.AddContent(39,
                $"{group.Label}: median {Num(s.Median)}, Q1 {Num(s.Q1)}, Q3 {Num(s.Q3)}, range {Num(s.Min)}–{Num(s.Max)}");
            builder.CloseElement();
            builder.CloseElement();

            builder.OpenElement(40, "line");
            builder.AddAttribute(41, "class", "dx-boxplot-median");
            builder.AddAttribute(42, "x1", F(cx - (boxWidth / 2)));
            builder.AddAttribute(43, "y1", F(Y(s.Median)));
            builder.AddAttribute(44, "x2", F(cx + (boxWidth / 2)));
            builder.AddAttribute(45, "y2", F(Y(s.Median)));
            builder.CloseElement();

            for (int o = 0; o < s.Outliers.Count; o++)
            {
                builder.OpenElement(50, "circle");
                builder.SetKey($"out{i}-{o}");
                builder.AddAttribute(51, "class", "dx-boxplot-outlier");
                builder.AddAttribute(52, "cx", F(cx));
                builder.AddAttribute(53, "cy", F(Y(s.Outliers[o])));
                builder.AddAttribute(54, "r", "3");
                builder.CloseElement();
            }
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private static void Whisker(RenderTreeBuilder builder, double cx, double yFrom, double yTo, double capHalfWidth)
    {
        builder.OpenElement(60, "line");
        builder.AddAttribute(61, "class", "dx-boxplot-whisker");
        builder.AddAttribute(62, "x1", F(cx));
        builder.AddAttribute(63, "y1", F(yFrom));
        builder.AddAttribute(64, "x2", F(cx));
        builder.AddAttribute(65, "y2", F(yTo));
        builder.CloseElement();

        builder.OpenElement(66, "line");
        builder.AddAttribute(67, "class", "dx-boxplot-whisker-cap");
        builder.AddAttribute(68, "x1", F(cx - capHalfWidth));
        builder.AddAttribute(69, "y1", F(yFrom));
        builder.AddAttribute(70, "x2", F(cx + capHalfWidth));
        builder.AddAttribute(71, "y2", F(yFrom));
        builder.CloseElement();
    }

    private static void BuildViolin(
        RenderTreeBuilder builder, int index, int[] bins, double cx, Func<double, double> y, double maxWidth,
        double axisMin, double axisMax, string color)
    {
        int peak = Math.Max(1, bins.Max());
        double binValueWidth = Math.Max(1e-9, axisMax - axisMin) / bins.Length;

        List<(double X, double Y)> left = [];
        List<(double X, double Y)> right = [];
        for (int b = 0; b < bins.Length; b++)
        {
            double loValue = axisMin + (b * binValueWidth);
            double hiValue = loValue + binValueWidth;
            double halfW = bins[b] / (double)peak * (maxWidth / 2);
            left.Add((cx - halfW, y(loValue)));
            left.Add((cx - halfW, y(hiValue)));
            right.Add((cx + halfW, y(loValue)));
            right.Add((cx + halfW, y(hiValue)));
        }

        right.Reverse();
        string points = string.Join(" ", left.Concat(right).Select(p => $"{F(p.X)},{F(p.Y)}"));

        builder.OpenElement(20, "polygon");
        builder.SetKey($"violin{index}");
        builder.AddAttribute(21, "class", "dx-boxplot-violin");
        builder.AddAttribute(22, "points", points);
        builder.AddAttribute(23, "fill", color);
        builder.CloseElement();
    }

    private static void Text(RenderTreeBuilder builder, double x, double y, string content)
    {
        builder.OpenElement(80, "text");
        builder.AddAttribute(81, "class", "dx-bar-label");
        builder.AddAttribute(82, "x", F(x));
        builder.AddAttribute(83, "y", F(y));
        builder.AddAttribute(84, "text-anchor", "middle");
        builder.AddContent(85, content);
        builder.CloseElement();
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
