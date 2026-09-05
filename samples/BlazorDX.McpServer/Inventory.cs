using BlazorDX.Primitives.Grid;

namespace BlazorDX.McpServer;

/// <summary>
/// A row in the sample's stock list. The <c>[GridColumn]</c> attributes are what a
/// <c>DxDataGrid</c> would render — and, through <see cref="BlazorDX.Primitives.Forms.GridAiTool{TRow}"/>,
/// they are also exactly what an assistant can see.
/// </summary>
/// <remarks>
/// <see cref="Cost"/> is deliberately not a column. It is a plain property with no attribute, so
/// it appears in neither the grid nor the tool schema, and no query can reach it. Narrowing what
/// an assistant may read is the same edit as narrowing what the grid shows, rather than a second
/// permission model kept in sync by hand.
/// </remarks>
[GridRow]
public sealed class StockRow
{
    [GridColumn("Sku", Order = 0)]
    public string Sku { get; set; } = string.Empty;

    [GridColumn("Product", Order = 1)]
    public string Product { get; set; } = string.Empty;

    [GridColumn("Warehouse", Order = 2)]
    public string Warehouse { get; set; } = string.Empty;

    [GridColumn("OnHand", Order = 3)]
    public int OnHand { get; set; }

    /// <summary>Not a <c>[GridColumn]</c>, so it is unreachable from the tool. See the remarks.</summary>
    public decimal Cost { get; set; }
}

/// <summary>
/// An in-memory stand-in for the server-side source a real grid would bind to — a database query,
/// a search index, an upstream API.
/// </summary>
/// <remarks>
/// The point of the sample is that this class is unchanged by being exposed to an assistant. It
/// implements <see cref="IGridDataSource{TRow}"/> because a grid needs paging and filtering on the
/// server; the AI tool then reuses that same contract, so the model is answered by the identical
/// code path — and the identical authorization — that renders the page.
/// </remarks>
internal sealed class StockDataSource : IGridDataSource<StockRow>
{
    private static readonly StockRow[] All =
    [
        new() { Sku = "AX-1001", Product = "Hex bolt M6", Warehouse = "Leeds", OnHand = 4820, Cost = 0.04m },
        new() { Sku = "AX-1002", Product = "Hex bolt M8", Warehouse = "Leeds", OnHand = 1290, Cost = 0.06m },
        new() { Sku = "AX-1044", Product = "Hex nut M8", Warehouse = "Leeds", OnHand = 0, Cost = 0.03m },
        new() { Sku = "BR-2201", Product = "Bearing 608ZZ", Warehouse = "Rotterdam", OnHand = 340, Cost = 1.15m },
        new() { Sku = "BR-2208", Product = "Bearing 6202", Warehouse = "Rotterdam", OnHand = 12, Cost = 1.80m },
        new() { Sku = "CB-3010", Product = "Cable tie 200mm", Warehouse = "Leeds", OnHand = 15600, Cost = 0.01m },
        new() { Sku = "CB-3015", Product = "Cable gland M20", Warehouse = "Rotterdam", OnHand = 870, Cost = 0.44m },
        new() { Sku = "SL-4400", Product = "Slide rail 300mm", Warehouse = "Gdansk", OnHand = 58, Cost = 7.90m },
        new() { Sku = "SL-4406", Product = "Slide rail 450mm", Warehouse = "Gdansk", OnHand = 6, Cost = 9.40m },
        new() { Sku = "WS-5000", Product = "Washer M6", Warehouse = "Gdansk", OnHand = 22400, Cost = 0.01m },
    ];

    public Task<GridDataPage<StockRow>> GetRowsAsync(GridDataRequest request, CancellationToken cancellationToken)
    {
        IEnumerable<StockRow> rows = All;

        foreach (GridColumnFilter filter in request.Filters)
        {
            string text = filter.Text;
            rows = rows.Where(row => Text(row, filter.Field).Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        // Applied in reverse so the first key is the outermost sort, matching what the grid does
        // with a multi-column header click.
        foreach (GridSortKey key in request.Sort.Reverse())
        {
            rows = key.Descending
                ? rows.OrderByDescending(row => Text(row, key.Field), StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(row => Text(row, key.Field), StringComparer.OrdinalIgnoreCase);
        }

        StockRow[] materialized = [.. rows];

        // The count is of the whole filtered set, not the page. The tool passes it straight
        // through, which is what lets a model say "12 matched, here are the first 5" instead of
        // reporting a page as if it were the answer.
        return Task.FromResult(new GridDataPage<StockRow>(
            [.. materialized.Skip(request.Skip).Take(request.Take)],
            materialized.Length));
    }

    private static string Text(StockRow row, string field) => field switch
    {
        "Sku" => row.Sku,
        "Product" => row.Product,
        "Warehouse" => row.Warehouse,
        // Padded so an ordinal comparison orders numbers numerically; a real source would sort in
        // the database and would not need this.
        "OnHand" => row.OnHand.ToString("D8", System.Globalization.CultureInfo.InvariantCulture),
        _ => string.Empty,
    };
}
