using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A network/force-directed graph: nodes settle into a stable layout (<see cref="ForceDirectedLayout"/>)
/// where connected nodes cluster together and unconnected ones drift apart. Pure SVG, pure C# — no
/// Rust/wasm kernel (see the layout's own remarks for why); styling via dx-chart.css.
/// </summary>
/// <remarks>Selection is opt-in, natural tab order — see <see cref="DxTreemap"/>'s remarks.</remarks>
public sealed class DxNetworkGraph : ComponentBase
{
    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    private IReadOnlyList<ForceNodeLayout> layout = [];
    private object? lastNodes;
    private object? lastEdges;

    [Parameter, EditorRequired] public IReadOnlyList<GraphNode> Nodes { get; set; } = [];

    [Parameter, EditorRequired] public IReadOnlyList<GraphEdge> Edges { get; set; } = [];

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 400;

    [Parameter] public int Iterations { get; set; } = 300;

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<GraphNode> OnNodeSelected { get; set; }

    private bool Interactive => OnNodeSelected.HasDelegate;

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(lastNodes, Nodes) && ReferenceEquals(lastEdges, Edges))
        {
            return;
        }

        lastNodes = Nodes;
        lastEdges = Edges;

        Dictionary<string, int> indexOf = Nodes.Select((n, i) => (n.Id, i)).ToDictionary(t => t.Id, t => t.i);
        List<ForceEdgeInput> edgeInputs = Edges
            .Where(e => indexOf.ContainsKey(e.Source) && indexOf.ContainsKey(e.Target))
            .Select(e => new ForceEdgeInput(indexOf[e.Source], indexOf[e.Target]))
            .ToList();

        layout = ForceDirectedLayout.Compute(Nodes.Count, edgeInputs, Width, Height, Iterations);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
        Dictionary<string, int> indexOf = Nodes.Select((n, i) => (n.Id, i)).ToDictionary(t => t.Id, t => t.i);

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", interactive ? "application" : "img");
        builder.AddAttribute(6, "aria-label", $"Network graph with {Nodes.Count} nodes and {Edges.Count} edges");

        for (int i = 0; i < Edges.Count; i++)
        {
            GraphEdge edge = Edges[i];
            if (!indexOf.TryGetValue(edge.Source, out int s) || !indexOf.TryGetValue(edge.Target, out int t) ||
                layout.Count <= Math.Max(s, t))
            {
                continue;
            }

            builder.OpenElement(10, "line");
            builder.SetKey($"edge{i}");
            builder.AddAttribute(11, "class", "dx-network-edge dx-chart-drawin");
            builder.AddAttribute(12, "x1", F(layout[s].X));
            builder.AddAttribute(13, "y1", F(layout[s].Y));
            builder.AddAttribute(14, "x2", F(layout[t].X));
            builder.AddAttribute(15, "y2", F(layout[t].Y));
            if (edge.Color is { } c)
            {
                builder.AddAttribute(16, "stroke", c);
            }

            builder.CloseElement();
        }

        for (int i = 0; i < Nodes.Count && i < layout.Count; i++)
        {
            GraphNode node = Nodes[i];
            ForceNodeLayout pos = layout[i];
            string color = node.Color ?? Palette[i % Palette.Length];

            builder.OpenElement(20, "circle");
            builder.SetKey($"node{i}");
            builder.AddAttribute(21, "class", "dx-network-node dx-chart-drawin");
            builder.AddAttribute(22, "cx", F(pos.X));
            builder.AddAttribute(23, "cy", F(pos.Y));
            builder.AddAttribute(24, "r", "9");
            builder.AddAttribute(25, "fill", color);
            builder.AddAttribute(26, "style", $"animation-delay:{i * 15}ms");

            if (interactive)
            {
                GraphNode captured = node;
                builder.AddAttribute(27, "tabindex", "0");
                builder.AddAttribute(39, "role", "button");
                builder.AddAttribute(28, "aria-label", node.Label);
                builder.AddAttribute(29, "onclick", EventCallback.Factory.Create(this, () => OnNodeSelected.InvokeAsync(captured)));
                builder.AddAttribute(30, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(
                    this, e => e.Key is "Enter" or " " ? OnNodeSelected.InvokeAsync(captured) : Task.CompletedTask));
            }

            builder.OpenElement(31, "title");
            builder.AddContent(32, node.Label);
            builder.CloseElement();
            builder.CloseElement();

            builder.OpenElement(33, "text");
            builder.AddAttribute(34, "class", "dx-network-label");
            builder.AddAttribute(35, "x", F(pos.X));
            builder.AddAttribute(36, "y", F(pos.Y - 13));
            builder.AddAttribute(37, "text-anchor", "middle");
            builder.AddContent(38, node.Label);
            builder.CloseElement();
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
