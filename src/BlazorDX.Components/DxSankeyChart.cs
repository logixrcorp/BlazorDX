using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A Sankey diagram: nodes as columns of rectangles, links as thickness-scaled ribbons flowing
/// between them. Layered layout via <see cref="SankeyLayout"/> — see its remarks for what
/// "layered" does and doesn't do (not full crossing-minimization). Pure SVG; styling via
/// dx-chart.css.
/// </summary>
/// <remarks>
/// Nodes are independently focusable (natural tab order) when <see cref="OnNodeSelected"/> is
/// wired — see <see cref="DxTreemap"/>'s remarks for why this differs from the roving-index
/// pattern the flat charts use. Links are not separately selectable, only hoverable via a title
/// tooltip — a ribbon has no single natural keyboard target the way a node rect does.
/// </remarks>
public sealed class DxSankeyChart : ComponentBase
{
    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    [Parameter, EditorRequired] public IReadOnlyList<SankeyNode> Nodes { get; set; } = [];

    [Parameter, EditorRequired] public IReadOnlyList<SankeyLink> Links { get; set; } = [];

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 360;

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<SankeyNode> OnNodeSelected { get; set; }

    private bool Interactive => OnNodeSelected.HasDelegate;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
        List<string> nodeIds = Nodes.Select(n => n.Id).ToList();
        List<SankeyLinkInput> linkInputs = Links.Select(l => new SankeyLinkInput(l.Source, l.Target, l.Value)).ToList();
        (IReadOnlyList<SankeyNodeLayout> nodeLayout, IReadOnlyList<SankeyLinkLayout> linkLayout) =
            SankeyLayout.Compute(nodeIds, linkInputs, Width, Height);

        Dictionary<string, SankeyNode> nodeById = Nodes.ToDictionary(n => n.Id);
        Dictionary<string, string> colorById = nodeIds
            .Select((id, i) => (id, color: nodeById[id].Color ?? Palette[i % Palette.Length]))
            .ToDictionary(t => t.id, t => t.color);

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", "img");
        builder.AddAttribute(6, "aria-label", $"Sankey diagram with {Nodes.Count} nodes and {Links.Count} flows");

        for (int i = 0; i < linkLayout.Count; i++)
        {
            SankeyLinkLayout link = linkLayout[i];
            SankeyLink source = Links[link.LinkIndex];
            string color = source.Color ?? colorById.GetValueOrDefault(source.Source, "#94a3b8");
            string title = $"{source.Source} → {source.Target}: {Num(source.Value)}";
            double midX = (link.SourceX + link.TargetX) / 2;

            builder.OpenElement(10, "path");
            builder.SetKey(i);
            builder.AddAttribute(11, "class", "dx-sankey-link dx-chart-drawin");
            builder.AddAttribute(12, "d",
                $"M {F(link.SourceX)} {F(link.SourceY)} C {F(midX)} {F(link.SourceY)} {F(midX)} {F(link.TargetY)} {F(link.TargetX)} {F(link.TargetY)}");
            builder.AddAttribute(13, "stroke", color);
            builder.AddAttribute(14, "stroke-width", F(link.Thickness));
            builder.AddAttribute(15, "fill", "none");
            builder.AddAttribute(16, "style", $"animation-delay:{i * 15}ms");
            builder.OpenElement(17, "title");
            builder.AddContent(18, title);
            builder.CloseElement();
            builder.CloseElement();
        }

        for (int i = 0; i < nodeLayout.Count; i++)
        {
            SankeyNodeLayout n = nodeLayout[i];
            SankeyNode node = nodeById[n.Id];
            string color = colorById[n.Id];
            string title = $"{node.Label}";

            builder.OpenElement(20, "rect");
            builder.SetKey($"node{i}");
            builder.AddAttribute(21, "class", "dx-sankey-node dx-chart-drawin");
            builder.AddAttribute(22, "x", F(n.X));
            builder.AddAttribute(23, "y", F(n.Y));
            builder.AddAttribute(24, "width", F(n.Width));
            builder.AddAttribute(25, "height", F(Math.Max(1, n.Height)));
            builder.AddAttribute(26, "fill", color);
            builder.AddAttribute(27, "style", $"animation-delay:{i * 20}ms");

            if (interactive)
            {
                builder.AddAttribute(28, "tabindex", "0");
                builder.AddAttribute(29, "aria-label", title);
                builder.AddAttribute(30, "onclick", EventCallback.Factory.Create(this, () => OnNodeSelected.InvokeAsync(node)));
                builder.AddAttribute(31, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(
                    this, e => e.Key is "Enter" or " " ? OnNodeSelected.InvokeAsync(node) : Task.CompletedTask));
            }

            builder.OpenElement(32, "title");
            builder.AddContent(33, title);
            builder.CloseElement();
            builder.CloseElement();

            bool labelOnRight = n.X < Width / 2.0;
            Text(builder, labelOnRight ? n.X + n.Width + 6 : n.X - 6, n.Y + (n.Height / 2) + 4, node.Label, labelOnRight ? "start" : "end");
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private static void Text(RenderTreeBuilder builder, double x, double y, string content, string anchor)
    {
        builder.OpenElement(40, "text");
        builder.AddAttribute(41, "class", "dx-sankey-label");
        builder.AddAttribute(42, "x", F(x));
        builder.AddAttribute(43, "y", F(y));
        builder.AddAttribute(44, "text-anchor", anchor);
        builder.AddContent(45, content);
        builder.CloseElement();
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
