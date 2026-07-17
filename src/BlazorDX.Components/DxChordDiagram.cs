using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A chord diagram: nodes as arcs around a circle (<see cref="ChordLayout"/>, sized by each node's
/// total involvement), links as ribbons connecting a proportional slice of each endpoint's arc.
/// Pure SVG; styling via dx-chart.css.
/// </summary>
/// <remarks>Node selection is opt-in, natural tab order — see <see cref="DxTreemap"/>'s remarks. Ribbons are hover-only (title tooltip).</remarks>
public sealed class DxChordDiagram : ComponentBase
{
    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    [Parameter, EditorRequired] public IReadOnlyList<ChordNode> Nodes { get; set; } = [];

    [Parameter, EditorRequired] public IReadOnlyList<ChordLink> Links { get; set; } = [];

    [Parameter] public int Size { get; set; } = 400;

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<ChordNode> OnNodeSelected { get; set; }

    private bool Interactive => OnNodeSelected.HasDelegate;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
        List<ChordLinkInput> linkInputs = Links.Select(l => new ChordLinkInput(l.From, l.To, l.Value)).ToList();
        (IReadOnlyList<ChordArc> arcs, IReadOnlyList<ChordRibbon> ribbons) = ChordLayout.Compute(Nodes.Count, linkInputs);

        double cx = Size / 2.0;
        double cy = Size / 2.0;
        double outerR = (Size / 2.0) - 34;
        double innerR = outerR - 14;

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Size} {Size}");
        builder.AddAttribute(5, "role", "img");
        builder.AddAttribute(6, "aria-label", $"Chord diagram with {Nodes.Count} nodes and {Links.Count} flows");

        for (int i = 0; i < ribbons.Count; i++)
        {
            ChordRibbon r = ribbons[i];
            string color = Nodes.Count > r.From ? (Nodes[r.From].Color ?? Palette[r.From % Palette.Length]) : "#94a3b8";
            string title = Nodes.Count > Math.Max(r.From, r.To)
                ? $"{Nodes[r.From].Label} → {Nodes[r.To].Label}: {Num(Links[r.LinkIndex].Value)}"
                : string.Empty;

            builder.OpenElement(10, "path");
            builder.SetKey($"ribbon{i}");
            builder.AddAttribute(11, "class", "dx-chord-ribbon dx-chart-drawin");
            builder.AddAttribute(12, "d", RibbonPath(cx, cy, innerR, r));
            builder.AddAttribute(13, "fill", color);
            builder.AddAttribute(14, "style", $"animation-delay:{i * 20}ms");
            builder.OpenElement(15, "title");
            builder.AddContent(16, title);
            builder.CloseElement();
            builder.CloseElement();
        }

        for (int i = 0; i < arcs.Count && i < Nodes.Count; i++)
        {
            ChordArc arc = arcs[i];
            ChordNode node = Nodes[i];
            string color = node.Color ?? Palette[i % Palette.Length];

            builder.OpenElement(20, "path");
            builder.SetKey($"arc{i}");
            builder.AddAttribute(21, "class", "dx-chord-arc dx-chart-drawin");
            builder.AddAttribute(22, "d", ArcPath(cx, cy, innerR, outerR, arc.StartAngle, arc.EndAngle));
            builder.AddAttribute(23, "fill", color);
            builder.AddAttribute(24, "style", $"animation-delay:{i * 10}ms");

            if (interactive)
            {
                ChordNode captured = node;
                builder.AddAttribute(25, "tabindex", "0");
                builder.AddAttribute(26, "aria-label", node.Label);
                builder.AddAttribute(27, "onclick", EventCallback.Factory.Create(this, () => OnNodeSelected.InvokeAsync(captured)));
                builder.AddAttribute(28, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(
                    this, e => e.Key is "Enter" or " " ? OnNodeSelected.InvokeAsync(captured) : Task.CompletedTask));
            }

            builder.OpenElement(29, "title");
            builder.AddContent(30, node.Label);
            builder.CloseElement();
            builder.CloseElement();

            double labelAngle = (arc.StartAngle + arc.EndAngle) / 2;
            (double lx, double ly) = Point(cx, cy, outerR + 12, labelAngle);
            builder.OpenElement(31, "text");
            builder.AddAttribute(32, "class", "dx-chord-label");
            builder.AddAttribute(33, "x", F(lx));
            builder.AddAttribute(34, "y", F(ly));
            builder.AddAttribute(35, "text-anchor", "middle");
            builder.AddContent(36, node.Label);
            builder.CloseElement();
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private static string ArcPath(double cx, double cy, double innerR, double outerR, double start, double end)
    {
        int largeArc = end - start > Math.PI ? 1 : 0;
        (double ox1, double oy1) = Point(cx, cy, outerR, start);
        (double ox2, double oy2) = Point(cx, cy, outerR, end);
        (double ix1, double iy1) = Point(cx, cy, innerR, start);
        (double ix2, double iy2) = Point(cx, cy, innerR, end);
        return
            $"M {F(ox1)} {F(oy1)} A {F(outerR)} {F(outerR)} 0 {largeArc} 1 {F(ox2)} {F(oy2)} " +
            $"L {F(ix2)} {F(iy2)} A {F(innerR)} {F(innerR)} 0 {largeArc} 0 {F(ix1)} {F(iy1)} Z";
    }

    // A ribbon: an arc along each endpoint's inner edge, joined by quadratic curves through the
    // circle's center — the classic chord-diagram "lens" shape.
    private static string RibbonPath(double cx, double cy, double innerR, ChordRibbon r)
    {
        int fromLarge = r.FromEnd - r.FromStart > Math.PI ? 1 : 0;
        int toLarge = r.ToEnd - r.ToStart > Math.PI ? 1 : 0;
        (double fx1, double fy1) = Point(cx, cy, innerR, r.FromStart);
        (double fx2, double fy2) = Point(cx, cy, innerR, r.FromEnd);
        (double tx1, double ty1) = Point(cx, cy, innerR, r.ToStart);
        (double tx2, double ty2) = Point(cx, cy, innerR, r.ToEnd);
        return
            $"M {F(fx1)} {F(fy1)} A {F(innerR)} {F(innerR)} 0 {fromLarge} 1 {F(fx2)} {F(fy2)} " +
            $"Q {F(cx)} {F(cy)} {F(tx1)} {F(ty1)} " +
            $"A {F(innerR)} {F(innerR)} 0 {toLarge} 1 {F(tx2)} {F(ty2)} " +
            $"Q {F(cx)} {F(cy)} {F(fx1)} {F(fy1)} Z";
    }

    private static (double X, double Y) Point(double cx, double cy, double r, double angle) =>
        (cx + (r * Math.Cos(angle - (Math.PI / 2))), cy + (r * Math.Sin(angle - (Math.PI / 2))));

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
