using System.Globalization;
using System.Text;
using System.Text.Json;
using BlazorDX.Primitives.Grid;

namespace BlazorDX.Primitives.Forms;

/// <summary>
/// Exposes an <see cref="IGridDataSource{TRow}"/> to an assistant as a read-only
/// <see cref="IAiTool"/>: the model can filter, sort and page the same server-side source the
/// grid binds to, and gets back the same rows the grid would have shown.
/// </summary>
/// <remarks>
/// <para>
/// <b>The columns are the contract.</b> Schema, filtering, sorting and output all come from the
/// generated <see cref="IGridRowAccessor{TRow}"/>, so the tool exposes exactly the columns the row
/// type declares with <c>[GridColumn]</c> and nothing else — no reflection, and no way for a
/// property that is not a column to reach the model. Narrowing what an assistant can see is
/// therefore the same act as narrowing what the grid shows.
/// </para>
/// <para>
/// Because the column set is known, the schema names the columns as an <c>enum</c> rather than
/// asking for a free-text field name. A model cannot then invent a column, and a wrong guess is a
/// schema violation the host rejects instead of a query that quietly matches nothing.
/// </para>
/// <para>
/// <b>Always read-only.</b> <see cref="IGridDataSource{TRow}"/> has no write path, and this tool
/// adds none. <see cref="IsReadOnly"/> is hard-coded rather than a constructor flag so it cannot be
/// set wrongly — a host that wants a writable grid tool has to write one deliberately.
/// </para>
/// <para>
/// <b>Rows are capped</b> by <c>maxRows</c>, and the reply always carries the unclamped
/// <c>totalCount</c>. A model that asks for everything gets a page plus an honest statement of how
/// much it did not see, which is what stops it reporting a partial answer as a complete one.
/// </para>
/// </remarks>
/// <typeparam name="TRow">The row type, bound through its generated accessor.</typeparam>
public sealed class GridAiTool<TRow> : IAiTool
{
    private readonly IGridRowAccessor<TRow> accessor;
    private readonly IGridDataSource<TRow> source;
    private readonly int maxRows;

    /// <param name="name">The tool name an assistant calls, e.g. <c>query_orders</c>.</param>
    /// <param name="description">What the data is. Worth writing well: it is the only thing
    /// telling the model when this tool is the right one to reach for.</param>
    /// <param name="accessor">The generated accessor for <typeparamref name="TRow"/>.</param>
    /// <param name="source">The same data source the grid binds to.</param>
    /// <param name="maxRows">Upper bound on rows per call, whatever the model asks for.</param>
    public GridAiTool(
        string name,
        string? description,
        IGridRowAccessor<TRow> accessor,
        IGridDataSource<TRow> source,
        int maxRows = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);

        Name = name;
        Description = description;
        this.accessor = accessor;
        this.source = source;
        this.maxRows = maxRows;
    }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Always <see langword="true"/>: this tool only reads.</summary>
    public bool IsReadOnly => true;

    public string InputSchemaJson
    {
        get
        {
            StringBuilder sb = new();
            sb.Append("{\"type\":\"object\",\"properties\":{");

            sb.Append("\"filters\":{\"type\":\"array\",\"description\":");
            AppendString(sb, "Case-insensitive substring matches, combined with AND.");
            sb.Append(",\"items\":{\"type\":\"object\",\"properties\":{\"column\":{");
            AppendColumnEnum(sb);
            sb.Append("},\"contains\":{\"type\":\"string\"}},\"required\":[\"column\",\"contains\"]}}");

            sb.Append(",\"sort\":{\"type\":\"array\",\"description\":");
            AppendString(sb, "Sort keys in priority order.");
            sb.Append(",\"items\":{\"type\":\"object\",\"properties\":{\"column\":{");
            AppendColumnEnum(sb);
            sb.Append("},\"descending\":{\"type\":\"boolean\",\"default\":false}},\"required\":[\"column\"]}}");

            sb.Append(",\"skip\":{\"type\":\"integer\",\"minimum\":0,\"default\":0,\"description\":");
            AppendString(sb, "Rows to skip, for paging through a larger result.");

            sb.Append("},\"take\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":");
            sb.Append(maxRows.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"description\":");
            AppendString(sb, $"Rows to return, at most {maxRows}.");
            sb.Append("}}}");

            return sb.ToString();
        }
    }

    public async Task<AiToolResult> InvokeAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        List<GridColumnFilter> filters = [];
        List<GridSortKey> sort = [];
        int skip = 0;
        int take = Math.Min(20, maxRows);

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new AiToolResult(true, "Arguments must be a JSON object.");
            }

            if (root.TryGetProperty("filters", out JsonElement filterElement)
                && filterElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in filterElement.EnumerateArray())
                {
                    string column = item.TryGetProperty("column", out JsonElement c) ? c.GetString() ?? string.Empty : string.Empty;
                    string text = item.TryGetProperty("contains", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty;

                    int index = IndexOfColumn(column);
                    if (index < 0)
                    {
                        return new AiToolResult(true, UnknownColumn(column));
                    }

                    filters.Add(new GridColumnFilter(index, accessor.Columns[index].Header, text));
                }
            }

            if (root.TryGetProperty("sort", out JsonElement sortElement)
                && sortElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in sortElement.EnumerateArray())
                {
                    string column = item.TryGetProperty("column", out JsonElement c) ? c.GetString() ?? string.Empty : string.Empty;
                    bool descending = item.TryGetProperty("descending", out JsonElement d)
                        && d.ValueKind == JsonValueKind.True;

                    int index = IndexOfColumn(column);
                    if (index < 0)
                    {
                        return new AiToolResult(true, UnknownColumn(column));
                    }

                    sort.Add(new GridSortKey(index, accessor.Columns[index].Header, descending));
                }
            }

            if (root.TryGetProperty("skip", out JsonElement skipElement)
                && skipElement.TryGetInt32(out int parsedSkip))
            {
                skip = Math.Max(0, parsedSkip);
            }

            if (root.TryGetProperty("take", out JsonElement takeElement)
                && takeElement.TryGetInt32(out int parsedTake))
            {
                // Clamped, not rejected: a model asking for 1000 rows wants as many as it can
                // have, and the reply's totalCount tells it what it is still missing.
                take = Math.Clamp(parsedTake, 1, maxRows);
            }
        }
        catch (JsonException ex)
        {
            return new AiToolResult(true, "Arguments are not valid JSON: " + ex.Message);
        }

        GridDataPage<TRow> page = await source
            .GetRowsAsync(new GridDataRequest(skip, take, sort, filters), cancellationToken)
            .ConfigureAwait(false);

        return new AiToolResult(false, Render(page, skip, take));
    }

    private string Render(GridDataPage<TRow> page, int skip, int take)
    {
        StringBuilder sb = new();
        sb.Append("{\"totalCount\":").Append(page.TotalCount.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"skip\":").Append(skip.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"take\":").Append(take.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"rows\":[");

        bool firstRow = true;
        foreach (TRow row in page.Rows)
        {
            if (!firstRow)
            {
                sb.Append(',');
            }

            firstRow = false;
            sb.Append('{');

            for (int column = 0; column < accessor.Columns.Count; column++)
            {
                if (column > 0)
                {
                    sb.Append(',');
                }

                AppendString(sb, accessor.Columns[column].Header);
                sb.Append(':');

                // Numeric columns are emitted as JSON numbers so a model can compare and total
                // them without re-parsing text it was handed. GetCellValue answers NaN for
                // anything it cannot express that way, which falls back to the display text.
                double value = accessor.Columns[column].IsNumeric
                    ? accessor.GetCellValue(row, column)
                    : double.NaN;

                if (double.IsFinite(value))
                {
                    sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
                }
                else
                {
                    AppendString(sb, accessor.GetCellText(row, column));
                }
            }

            sb.Append('}');
        }

        sb.Append("]}");
        return sb.ToString();
    }

    private int IndexOfColumn(string header)
    {
        for (int i = 0; i < accessor.Columns.Count; i++)
        {
            if (string.Equals(accessor.Columns[i].Header, header, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    // Naming the real columns back: a model that guessed wrong can correct itself from the error
    // rather than retrying blind.
    private string UnknownColumn(string column) =>
        $"Unknown column \"{column}\". Available columns: "
        + string.Join(", ", accessor.Columns.Select(c => c.Header)) + ".";

    private void AppendColumnEnum(StringBuilder sb)
    {
        sb.Append("\"type\":\"string\",\"enum\":[");
        for (int i = 0; i < accessor.Columns.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            AppendString(sb, accessor.Columns[i].Header);
        }

        sb.Append(']');
    }

    private static void AppendString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }
}
