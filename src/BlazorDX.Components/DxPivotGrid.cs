using System.Globalization;
using BlazorDX.Compute;
using BlazorDX.Primitives.Grid;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled pivot table built on <see cref="PivotGridPrimitive{TRow}"/>. It
/// renders a cross-tab: pivot-row keys down the side, pivot-column keys across the
/// top, aggregated value cells in the body, and row/column/grand totals. Styling
/// is class- and CSS-variable-based (see dx-datagrid.css).
/// </summary>
/// <typeparam name="TRow">The row type, bound via a generated accessor.</typeparam>
public sealed class DxPivotGrid<TRow> : PivotGridPrimitive<TRow>
{
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "table");
        builder.AddAttribute(1, "class", $"dx-pivot {Class}".TrimEnd());

        builder.OpenElement(2, "caption");
        builder.AddAttribute(3, "class", "dx-pivot-caption");
        builder.AddContent(4, $"{Aggregate} of {ValueFieldHeader} · {Backend}");
        builder.CloseElement();

        BuildHead(builder);
        BuildBody(builder);
        BuildFoot(builder);

        builder.CloseElement();
    }

    private void BuildHead(RenderTreeBuilder builder)
    {
        builder.OpenElement(5, "thead");
        builder.OpenElement(6, "tr");

        builder.OpenElement(7, "th");
        builder.AddAttribute(8, "class", "dx-pivot-corner");
        builder.AddAttribute(9, "scope", "col");
        builder.AddContent(10, $"{RowFieldHeader} \\ {ColumnFieldHeader}");
        builder.CloseElement();

        foreach (string colKey in ColumnKeys)
        {
            builder.OpenElement(11, "th");
            builder.SetKey(colKey);
            builder.AddAttribute(12, "class", "dx-pivot-colhead");
            builder.AddAttribute(13, "scope", "col");
            builder.AddContent(14, colKey);
            builder.CloseElement();
        }

        builder.OpenElement(15, "th");
        builder.AddAttribute(16, "class", "dx-pivot-colhead dx-pivot-total");
        builder.AddAttribute(17, "scope", "col");
        builder.AddContent(18, "Total");
        builder.CloseElement();

        builder.CloseElement();
        builder.CloseElement();
    }

    private void BuildBody(RenderTreeBuilder builder)
    {
        builder.OpenElement(19, "tbody");

        foreach (string rowKey in RowKeys)
        {
            builder.OpenElement(20, "tr");
            builder.SetKey(rowKey);

            builder.OpenElement(21, "th");
            builder.AddAttribute(22, "class", "dx-pivot-rowhead");
            builder.AddAttribute(23, "scope", "row");
            builder.AddContent(24, rowKey);
            builder.CloseElement();

            foreach (string colKey in ColumnKeys)
            {
                builder.OpenElement(25, "td");
                builder.SetKey(colKey);
                builder.AddAttribute(26, "class", "dx-pivot-cell");
                builder.AddContent(27, Format(Cell(rowKey, colKey)));
                builder.CloseElement();
            }

            builder.OpenElement(28, "td");
            builder.AddAttribute(29, "class", "dx-pivot-cell dx-pivot-total");
            builder.AddContent(30, Format(RowTotal(rowKey)));
            builder.CloseElement();

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildFoot(RenderTreeBuilder builder)
    {
        builder.OpenElement(31, "tfoot");
        builder.OpenElement(32, "tr");

        builder.OpenElement(33, "th");
        builder.AddAttribute(34, "class", "dx-pivot-rowhead dx-pivot-total");
        builder.AddAttribute(35, "scope", "row");
        builder.AddContent(36, "Total");
        builder.CloseElement();

        foreach (string colKey in ColumnKeys)
        {
            builder.OpenElement(37, "td");
            builder.SetKey(colKey);
            builder.AddAttribute(38, "class", "dx-pivot-cell dx-pivot-total");
            builder.AddContent(39, Format(ColumnTotal(colKey)));
            builder.CloseElement();
        }

        builder.OpenElement(40, "td");
        builder.AddAttribute(41, "class", "dx-pivot-cell dx-pivot-grand");
        builder.AddContent(42, Format(GrandTotal));
        builder.CloseElement();

        builder.CloseElement();
        builder.CloseElement();
    }

    private string Format(double value)
    {
        if (double.IsNaN(value))
        {
            return string.Empty;
        }

        return Aggregate == GridAggregateKind.Count
            ? value.ToString("N0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
