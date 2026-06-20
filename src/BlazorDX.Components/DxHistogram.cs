using System.Globalization;
using BlazorDX.Compute;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A histogram: bins a raw value series into equal-width buckets and draws the
/// per-bin counts as bars. The binning runs on the compute backend (Rust in the
/// browser, managed C# on the server), so large datasets bin off the UI thread.
/// Styling is token-driven (see dx-chart.css).
/// </summary>
public sealed class DxHistogram : ComponentBase
{
    private int[] counts = [];
    private double min;
    private double max;
    private object? lastSeries;

    [Parameter, EditorRequired] public IReadOnlyList<double> Values { get; set; } = [];

    [Parameter] public int Bins { get; set; } = 12;

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 240;

    [Parameter] public string? Class { get; set; }

    [Inject] private IGridCompute Compute { get; set; } = default!;

    protected override async Task OnParametersSetAsync()
    {
        if (!ReferenceEquals(lastSeries, Values))
        {
            lastSeries = Values;
            if (Values.Count == 0 || Bins <= 0)
            {
                counts = [];
                return;
            }

            double[] data = Values.ToArray();
            min = data.Min();
            max = data.Max();
            counts = await Compute.HistogramAsync(data, Bins, min, max);
            StateHasChanged();
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", "img");
        builder.AddAttribute(6, "aria-label", $"Histogram with {counts.Length} bins");

        if (counts.Length > 0)
        {
            BuildBars(builder);
        }

        builder.CloseElement();

        builder.OpenElement(20, "div");
        builder.AddAttribute(21, "class", "dx-chart-caption");
        builder.AddContent(22,
            $"{Values.Count:N0} values · {counts.Length} bins · range {Num(min)}–{Num(max)} · {Compute.Backend}");
        builder.CloseElement();

        builder.CloseElement();
    }

    private void BuildBars(RenderTreeBuilder builder)
    {
        double axis = Height - 20;
        double slot = (double)Width / counts.Length;
        double barWidth = Math.Max(1, slot - 3);
        int peak = Math.Max(1, counts.Max());

        for (int i = 0; i < counts.Length; i++)
        {
            double h = counts[i] / (double)peak * (axis - 14);
            double x = (i * slot) + 1.5;
            double y = axis - h;

            builder.OpenElement(10, "rect");
            builder.AddAttribute(11, "class", "dx-bar-rect");
            builder.AddAttribute(12, "x", F(x));
            builder.AddAttribute(13, "y", F(y));
            builder.AddAttribute(14, "width", F(barWidth));
            builder.AddAttribute(15, "height", F(Math.Max(0, h)));
            builder.AddAttribute(16, "fill", "var(--dx-accent, #2563eb)");
            builder.OpenElement(17, "title");
            builder.AddContent(18, $"{BinLabel(i)}: {counts[i]}");
            builder.CloseElement();
            builder.CloseElement();
        }
    }

    private string BinLabel(int i)
    {
        double width = (max - min) / counts.Length;
        double lo = min + (i * width);
        double hi = lo + width;
        return $"{Num(lo)}–{Num(hi)}";
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
