using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A parallel coordinates chart: one vertical axis per dimension, each row drawn as a polyline
/// crossing every axis at its own value (independently min/max-normalized per axis). Good for
/// spotting clusters and correlations across many dimensions at once — something no single 2-D
/// chart in the family shows. Pure SVG; styling via dx-chart.css.
/// </summary>
public sealed class DxParallelCoordinates : ComponentBase
{
    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    /// <summary>Dimension names, left to right; each <see cref="ParallelCoordinateRow.Values"/> must have one value per axis.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<string> Axes { get; set; } = [];

    [Parameter, EditorRequired] public IReadOnlyList<ParallelCoordinateRow> Rows { get; set; } = [];

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 320;

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<ParallelCoordinateRow> OnRowSelected { get; set; }

    private bool Interactive => OnRowSelected.HasDelegate;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
        int n = Axes.Count;

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", "img");
        builder.AddAttribute(6, "aria-label", $"Parallel coordinates chart of {Rows.Count} rows over {n} axes");

        if (n < 2 || Rows.Count == 0)
        {
            builder.CloseElement();
            builder.CloseElement();
            return;
        }

        const double top = 16;
        const double bottom = 26;
        double plotH = Height - top - bottom;
        double left = 12;
        double right = Width - 12;

        double X(int a) => n == 1 ? left : left + (a * (right - left) / (n - 1));

        double[] min = new double[n];
        double[] max = new double[n];
        for (int a = 0; a < n; a++)
        {
            min[a] = Rows.Min(r => r.Values.Count > a ? r.Values[a] : 0);
            max[a] = Rows.Max(r => r.Values.Count > a ? r.Values[a] : 0);
            if (max[a] - min[a] < 1e-9)
            {
                max[a] = min[a] + 1;
            }
        }

        double Y(int a, double value) => top + plotH - ((value - min[a]) / (max[a] - min[a]) * plotH);

        for (int a = 0; a < n; a++)
        {
            builder.OpenElement(10, "line");
            builder.SetKey($"axis{a}");
            builder.AddAttribute(11, "class", "dx-parallel-axis");
            builder.AddAttribute(12, "x1", F(X(a)));
            builder.AddAttribute(13, "y1", F(top));
            builder.AddAttribute(14, "x2", F(X(a)));
            builder.AddAttribute(15, "y2", F(top + plotH));
            builder.CloseElement();

            builder.OpenElement(16, "text");
            builder.AddAttribute(17, "class", "dx-parallel-axis-label");
            builder.AddAttribute(18, "x", F(X(a)));
            builder.AddAttribute(19, "y", F(Height - 6));
            builder.AddAttribute(20, "text-anchor", "middle");
            builder.AddContent(21, Axes[a]);
            builder.CloseElement();
        }

        for (int r = 0; r < Rows.Count; r++)
        {
            ParallelCoordinateRow row = Rows[r];
            string color = row.Color ?? Palette[r % Palette.Length];
            StringBuilder points = new();
            for (int a = 0; a < n; a++)
            {
                double value = row.Values.Count > a ? row.Values[a] : min[a];
                points.Append(F(X(a))).Append(',').Append(F(Y(a, value))).Append(' ');
            }

            string css = "dx-parallel-line dx-chart-reveal";
            builder.OpenElement(30, "polyline");
            builder.SetKey($"row{r}");
            builder.AddAttribute(31, "class", css);
            builder.AddAttribute(32, "points", points.ToString().TrimEnd());
            builder.AddAttribute(33, "fill", "none");
            builder.AddAttribute(34, "stroke", color);

            if (interactive)
            {
                ParallelCoordinateRow captured = row;
                builder.AddAttribute(35, "tabindex", "0");
                builder.AddAttribute(36, "aria-label", row.Label);
                builder.AddAttribute(37, "onclick", EventCallback.Factory.Create(this, () => OnRowSelected.InvokeAsync(captured)));
            }

            builder.OpenElement(38, "title");
            builder.AddContent(39, row.Label);
            builder.CloseElement();
            builder.CloseElement();
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
