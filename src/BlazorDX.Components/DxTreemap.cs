using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A treemap: nested rectangles sized proportionally to <see cref="ChartTreeNode.Value"/>, laid out
/// with the squarified algorithm (<see cref="TreemapLayout"/>) so cells stay close to square
/// instead of degenerating into slivers. Only leaves are drawn; a leaf's color comes from its
/// nearest ancestor with a <see cref="ChartTreeNode.Color"/> override, or a palette color assigned
/// per top-level branch. Pure SVG; styling via dx-chart.css.
/// </summary>
/// <remarks>
/// Selection is opt-in like the rest of the chart family, but — unlike the flat charts' roving
/// <see cref="Primitives.Charts.ChartSelectionPrimitive"/> — each cell is independently focusable
/// (natural tab order), since a nested hierarchy doesn't reduce to one linear index the way a bar
/// or slice list does.
/// </remarks>
public sealed class DxTreemap : ComponentBase
{
    private readonly string chartId = $"dx-treemap-{Guid.NewGuid():N}";

    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    [Parameter, EditorRequired] public ChartTreeNode Root { get; set; } = new("Root");

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 360;

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<ChartTreeNodeEventArgs> OnNodeSelected { get; set; }

    private bool Interactive => OnNodeSelected.HasDelegate;

    private readonly record struct Cell(ChartTreeNode Node, double X, double Y, double Width, double Height, IReadOnlyList<string> Path, string Color);

    private static double EffectiveValue(ChartTreeNode node) =>
        node.Children is { Count: > 0 } children ? children.Sum(EffectiveValue) : Math.Max(0, node.Value);

    private List<Cell> Flatten()
    {
        List<Cell> output = [];
        if (Root.Children is not { Count: > 0 } topChildren)
        {
            return output;
        }

        double[] values = topChildren.Select(EffectiveValue).ToArray();
        IReadOnlyList<TreemapRect> rects = TreemapLayout.Compute(values, 0, 0, Width, Height);
        foreach (TreemapRect r in rects)
        {
            ChartTreeNode child = topChildren[r.Index];
            string color = child.Color ?? Palette[r.Index % Palette.Length];
            LayoutRecursive(child, r.X, r.Y, r.Width, r.Height, [child.Label], color, output);
        }

        return output;
    }

    private static void LayoutRecursive(
        ChartTreeNode node, double x, double y, double w, double h, IReadOnlyList<string> path, string color,
        List<Cell> output)
    {
        if (node.Children is not { Count: > 0 } children)
        {
            output.Add(new Cell(node, x, y, w, h, path, node.Color ?? color));
            return;
        }

        double[] values = children.Select(EffectiveValue).ToArray();
        IReadOnlyList<TreemapRect> rects = TreemapLayout.Compute(values, x, y, w, h);
        foreach (TreemapRect r in rects)
        {
            ChartTreeNode child = children[r.Index];
            LayoutRecursive(child, r.X, r.Y, r.Width, r.Height, [.. path, child.Label], child.Color ?? color, output);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
        List<Cell> cells = Flatten();

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", interactive ? "application" : "img");
        builder.AddAttribute(6, "aria-label", $"Treemap with {cells.Count} cells");

        for (int i = 0; i < cells.Count; i++)
        {
            Cell cell = cells[i];
            string label = cell.Node.Label;
            string title = $"{string.Join(" / ", cell.Path)}: {Num(EffectiveValue(cell.Node))}";

            string css = "dx-treemap-cell dx-chart-drawin";
            builder.OpenElement(10, "rect");
            builder.SetKey(i);
            builder.AddAttribute(11, "class", css);
            builder.AddAttribute(12, "x", F(cell.X));
            builder.AddAttribute(13, "y", F(cell.Y));
            builder.AddAttribute(14, "width", F(Math.Max(0, cell.Width - 1)));
            builder.AddAttribute(15, "height", F(Math.Max(0, cell.Height - 1)));
            builder.AddAttribute(16, "fill", cell.Color);
            builder.AddAttribute(17, "style", $"animation-delay:{i * 12}ms");

            if (interactive)
            {
                int captured = i;
                builder.AddAttribute(18, "tabindex", "0");
                builder.AddAttribute(24, "role", "button");
                builder.AddAttribute(19, "aria-label", title);
                builder.AddAttribute(20, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(cells, captured)));
                builder.AddAttribute(21, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(
                    this, e => OnCellKeyDownAsync(e, cells, captured)));
            }

            builder.OpenElement(22, "title");
            builder.AddContent(23, title);
            builder.CloseElement();
            builder.CloseElement();

            if (cell.Width > 44 && cell.Height > 18)
            {
                Text(builder, cell.X + 4, cell.Y + 14, label);
            }
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private static void Text(RenderTreeBuilder builder, double x, double y, string content)
    {
        builder.OpenElement(30, "text");
        builder.AddAttribute(31, "class", "dx-treemap-label");
        builder.AddAttribute(32, "x", F(x));
        builder.AddAttribute(33, "y", F(y));
        builder.AddContent(34, content);
        builder.CloseElement();
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private Task SelectAsync(List<Cell> cells, int index) =>
        OnNodeSelected.InvokeAsync(new ChartTreeNodeEventArgs(cells[index].Node, cells[index].Path));

    private Task OnCellKeyDownAsync(KeyboardEventArgs args, List<Cell> cells, int index) =>
        args.Key is "Enter" or " " ? SelectAsync(cells, index) : Task.CompletedTask;
}

/// <summary>The node a click/Enter/Space interaction occurred on, for <see cref="DxTreemap"/>/<see cref="DxSunburst"/>.</summary>
/// <param name="Node">The node itself.</param>
/// <param name="Path">Breadcrumb of labels from the root's direct child down to (and including) this node.</param>
public readonly record struct ChartTreeNodeEventArgs(ChartTreeNode Node, IReadOnlyList<string> Path);
