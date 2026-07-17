using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A sunburst: a radial treemap — every node (not just leaves) draws as its own ring segment at
/// its depth, angular width proportional to <see cref="ChartTreeNode.Value"/> (via
/// <see cref="SunburstLayout"/>). Reads the same <see cref="ChartTreeNode"/> shape as
/// <see cref="DxTreemap"/>. Pure SVG; styling via dx-chart.css.
/// </summary>
/// <remarks>Selection is opt-in, natural tab order — see <see cref="DxTreemap"/>'s remarks.</remarks>
public sealed class DxSunburst : ComponentBase
{
    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    [Parameter, EditorRequired] public ChartTreeNode Root { get; set; } = new("Root");

    [Parameter] public int Size { get; set; } = 360;

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<ChartTreeNodeEventArgs> OnNodeSelected { get; set; }

    private bool Interactive => OnNodeSelected.HasDelegate;

    private readonly record struct Arc(ChartTreeNode Node, double Start, double End, int Depth, IReadOnlyList<string> Path, string Color);

    private static double EffectiveValue(ChartTreeNode node) =>
        node.Children is { Count: > 0 } children ? children.Sum(EffectiveValue) : Math.Max(0, node.Value);

    private static int MaxDepth(ChartTreeNode node) =>
        node.Children is { Count: > 0 } children ? 1 + children.Max(MaxDepth) : 0;

    private List<Arc> Flatten()
    {
        List<Arc> output = [];
        if (Root.Children is not { Count: > 0 } topChildren)
        {
            return output;
        }

        double[] values = topChildren.Select(EffectiveValue).ToArray();
        IReadOnlyList<SunburstSlice> slices = SunburstLayout.Compute(values, 0, 2 * Math.PI);
        foreach (SunburstSlice s in slices)
        {
            ChartTreeNode child = topChildren[s.Index];
            string color = child.Color ?? Palette[s.Index % Palette.Length];
            LayoutRecursive(child, s.StartAngle, s.EndAngle, 1, [child.Label], color, output);
        }

        return output;
    }

    private static void LayoutRecursive(
        ChartTreeNode node, double start, double end, int depth, IReadOnlyList<string> path, string color, List<Arc> output)
    {
        output.Add(new Arc(node, start, end, depth, path, node.Color ?? color));
        if (node.Children is not { Count: > 0 } children)
        {
            return;
        }

        double[] values = children.Select(EffectiveValue).ToArray();
        IReadOnlyList<SunburstSlice> slices = SunburstLayout.Compute(values, start, end);
        foreach (SunburstSlice s in slices)
        {
            ChartTreeNode child = children[s.Index];
            LayoutRecursive(child, s.StartAngle, s.EndAngle, depth + 1, [.. path, child.Label], child.Color ?? color, output);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
        List<Arc> arcs = Flatten();
        int maxDepth = Math.Max(1, MaxDepth(Root));
        double cx = Size / 2.0;
        double cy = Size / 2.0;
        double hubR = Size * 0.08;
        double maxR = (Size / 2.0) - 4;
        double ringThickness = (maxR - hubR) / maxDepth;

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Size} {Size}");
        builder.AddAttribute(5, "role", "img");
        builder.AddAttribute(6, "aria-label", $"Sunburst chart with {arcs.Count} segments");

        builder.OpenElement(7, "circle");
        builder.AddAttribute(8, "class", "dx-sunburst-hub");
        builder.AddAttribute(9, "cx", F(cx));
        builder.AddAttribute(10, "cy", F(cy));
        builder.AddAttribute(11, "r", F(hubR));
        builder.OpenElement(12, "title");
        builder.AddContent(13, $"{Root.Label}: {Num(EffectiveValue(Root))}");
        builder.CloseElement();
        builder.CloseElement();

        for (int i = 0; i < arcs.Count; i++)
        {
            Arc arc = arcs[i];
            double innerR = hubR + ((arc.Depth - 1) * ringThickness);
            double outerR = innerR + ringThickness;
            string title = $"{string.Join(" / ", arc.Path)}: {Num(EffectiveValue(arc.Node))}";

            builder.OpenElement(20, "path");
            builder.SetKey(i);
            builder.AddAttribute(21, "class", "dx-sunburst-arc dx-chart-drawin");
            builder.AddAttribute(22, "d", ArcPath(cx, cy, innerR, outerR, arc.Start, arc.End));
            builder.AddAttribute(23, "fill", arc.Color);
            builder.AddAttribute(24, "style", $"animation-delay:{i * 10}ms");

            if (interactive)
            {
                int captured = i;
                builder.AddAttribute(25, "tabindex", "0");
                builder.AddAttribute(26, "aria-label", title);
                builder.AddAttribute(27, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(arcs, captured)));
                builder.AddAttribute(28, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(
                    this, e => OnArcKeyDownAsync(e, arcs, captured)));
            }

            builder.OpenElement(29, "title");
            builder.AddContent(30, title);
            builder.CloseElement();
            builder.CloseElement();
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    // An annulus segment; a segment spanning (near) the full circle is split into two half-arcs
    // to avoid the degenerate same-point arc SVG renders as nothing (same issue DxPieChart guards).
    private static string ArcPath(double cx, double cy, double innerR, double outerR, double start, double end)
    {
        if (end - start >= (2 * Math.PI) - 0.001)
        {
            double mid = start + Math.PI;
            return ArcPath(cx, cy, innerR, outerR, start, mid) + " " + ArcPath(cx, cy, innerR, outerR, mid, end);
        }

        int largeArc = end - start > Math.PI ? 1 : 0;
        (double ox1, double oy1) = Point(cx, cy, outerR, start);
        (double ox2, double oy2) = Point(cx, cy, outerR, end);
        (double ix1, double iy1) = Point(cx, cy, innerR, start);
        (double ix2, double iy2) = Point(cx, cy, innerR, end);
        return
            $"M {F(ox1)} {F(oy1)} A {F(outerR)} {F(outerR)} 0 {largeArc} 1 {F(ox2)} {F(oy2)} " +
            $"L {F(ix2)} {F(iy2)} A {F(innerR)} {F(innerR)} 0 {largeArc} 0 {F(ix1)} {F(iy1)} Z";
    }

    private static (double X, double Y) Point(double cx, double cy, double r, double angle) =>
        (cx + (r * Math.Cos(angle - (Math.PI / 2))), cy + (r * Math.Sin(angle - (Math.PI / 2))));

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private Task SelectAsync(List<Arc> arcs, int index) =>
        OnNodeSelected.InvokeAsync(new ChartTreeNodeEventArgs(arcs[index].Node, arcs[index].Path));

    private Task OnArcKeyDownAsync(KeyboardEventArgs args, List<Arc> arcs, int index) =>
        args.Key is "Enter" or " " ? SelectAsync(arcs, index) : Task.CompletedTask;
}
