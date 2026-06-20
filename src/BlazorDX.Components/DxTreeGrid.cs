using System.Globalization;
using BlazorDX.Primitives.Grid;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled tree grid: a hierarchical table built on
/// <see cref="TreeGridPrimitive{TRow}"/>. The first column carries the expand/
/// collapse twisty and indentation; the rest render like a normal grid. Markup
/// uses <see cref="RenderTreeBuilder"/> with static sequence numbers and the
/// inherited virtualization. Styling is CSS-variable-based (see dx-datagrid.css).
/// </summary>
/// <typeparam name="TRow">The node type (a reference type).</typeparam>
public sealed class DxTreeGrid<TRow> : TreeGridPrimitive<TRow>
    where TRow : class
{
    private const int IndentPerLevel = 18;

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        string columnTemplate =
            $"display:grid;grid-template-columns:repeat({Columns.Count},minmax(0,1fr));";

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", ContainerId);
        builder.AddAttribute(2, "class", $"dx-grid dx-tree-grid {Class}".TrimEnd());
        builder.AddAttribute(3, "role", "treegrid");
        builder.AddAttribute(4, "aria-rowcount", VisibleRowCount);
        builder.AddAttribute(5, "style", $"height:{ViewportHeight}px;");

        BuildHeader(builder, columnTemplate);
        BuildBody(builder, columnTemplate);

        builder.CloseElement();
    }

    private void BuildHeader(RenderTreeBuilder builder, string columnTemplate)
    {
        builder.OpenElement(6, "div");
        builder.AddAttribute(7, "class", "dx-grid-header");
        builder.AddAttribute(8, "role", "row");
        builder.AddAttribute(9, "style", columnTemplate);

        for (int i = 0; i < Columns.Count; i++)
        {
            builder.OpenElement(10, "div");
            builder.AddAttribute(11, "class", "dx-grid-th");
            builder.AddAttribute(12, "role", "columnheader");

            builder.OpenElement(13, "span");
            builder.AddAttribute(14, "class", "dx-grid-th-label");
            builder.AddContent(15, Columns[i].Header);
            builder.CloseElement();

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildBody(RenderTreeBuilder builder, string columnTemplate)
    {
        builder.OpenElement(16, "div");
        builder.AddAttribute(17, "class", "dx-grid-body");

        builder.OpenElement(18, "div");
        builder.AddAttribute(19, "style", $"height:{TopPadding}px;");
        builder.CloseElement();

        foreach (TreeGridRow<TRow> node in VisibleRows())
        {
            BuildRow(builder, node, columnTemplate);
        }

        builder.OpenElement(20, "div");
        builder.AddAttribute(21, "style", $"height:{BottomPadding}px;");
        builder.CloseElement();

        builder.CloseElement();
    }

    private void BuildRow(RenderTreeBuilder builder, TreeGridRow<TRow> node, string columnTemplate)
    {
        TRow row = node.Row;

        builder.OpenElement(22, "div");
        builder.SetKey(row);
        builder.AddAttribute(23, "class", "dx-grid-row");
        builder.AddAttribute(24, "role", "row");
        builder.AddAttribute(25, "tabindex", "0");
        builder.AddAttribute(26, "aria-level", node.Depth + 1);
        if (node.HasChildren)
        {
            builder.AddAttribute(27, "aria-expanded", node.Expanded ? "true" : "false");
        }

        builder.AddAttribute(28, "style", $"height:{RowHeight}px;{columnTemplate}");
        builder.AddAttribute(29, "onkeydown",
            EventCallback.Factory.Create<KeyboardEventArgs>(this, e => OnRowKeyDown(row, e.Key)));

        for (int column = 0; column < Columns.Count; column++)
        {
            builder.OpenElement(30, "div");
            builder.AddAttribute(31, "class", "dx-grid-cell");
            builder.AddAttribute(32, "role", "gridcell");

            if (column == 0)
            {
                BuildTreeCell(builder, node);
            }
            else
            {
                builder.AddContent(40, CellText(row, column));
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    // The first column: indentation, an expand twisty (parents only), then text.
    private void BuildTreeCell(RenderTreeBuilder builder, TreeGridRow<TRow> node)
    {
        builder.OpenElement(33, "span");
        builder.AddAttribute(34, "class", "dx-tree-cell");
        builder.AddAttribute(35, "style",
            string.Create(CultureInfo.InvariantCulture, $"padding-left:{node.Depth * IndentPerLevel}px;"));

        if (node.HasChildren)
        {
            TRow row = node.Row;
            builder.OpenElement(36, "button");
            builder.AddAttribute(37, "type", "button");
            builder.AddAttribute(38, "class", "dx-tree-toggle");
            builder.AddAttribute(39, "aria-label", node.Expanded ? "Collapse" : "Expand");
            builder.AddAttribute(41, "onclick", EventCallback.Factory.Create(this, () => Toggle(row)));
            builder.AddContent(42, node.Expanded ? "▾" : "▸");
            builder.CloseElement();
        }
        else
        {
            builder.OpenElement(43, "span");
            builder.AddAttribute(44, "class", "dx-tree-spacer");
            builder.AddAttribute(45, "aria-hidden", "true");
            builder.CloseElement();
        }

        builder.OpenElement(46, "span");
        builder.AddAttribute(47, "class", "dx-tree-label");
        builder.AddContent(48, CellText(node.Row, 0));
        builder.CloseElement();

        builder.CloseElement();
    }
}
