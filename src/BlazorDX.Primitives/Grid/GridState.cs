using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace BlazorDX.Primitives.Grid;

/// <summary>A column's sort within saved grid state.</summary>
public sealed record GridSortState(int Column, bool Descending);

/// <summary>A column's text filter within saved grid state.</summary>
public sealed record GridFilterState(int Column, string Text);

/// <summary>
/// A serializable snapshot of a <see cref="DataGridPrimitive{TRow}"/>'s layout — column
/// order, widths, hidden columns, sort, and column filters. Capture it with
/// <c>CaptureState()</c>, persist <see cref="ToJson"/> (localStorage, a user profile,
/// …), and restore later with <c>ApplyStateAsync</c>. JSON is built/parsed by hand
/// (no reflection), so it stays trim- and AOT-safe.
/// </summary>
public sealed class GridState
{
    public IReadOnlyList<int> ColumnOrder { get; init; } = Array.Empty<int>();
    public IReadOnlyList<double> ColumnWidths { get; init; } = Array.Empty<double>();
    public IReadOnlyList<int> HiddenColumns { get; init; } = Array.Empty<int>();
    public IReadOnlyList<GridSortState> Sort { get; init; } = Array.Empty<GridSortState>();
    public IReadOnlyList<GridFilterState> Filters { get; init; } = Array.Empty<GridFilterState>();

    /// <summary>Serializes the state to JSON for persistence.</summary>
    public string ToJson()
    {
        StringBuilder sb = new();
        sb.Append('{');
        AppendInts(sb, "columnOrder", ColumnOrder);
        sb.Append(',');
        AppendDoubles(sb, "columnWidths", ColumnWidths);
        sb.Append(',');
        AppendInts(sb, "hiddenColumns", HiddenColumns);

        sb.Append(",\"sort\":[");
        for (int i = 0; i < Sort.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append("{\"column\":").Append(Sort[i].Column)
              .Append(",\"descending\":").Append(Sort[i].Descending ? "true" : "false").Append('}');
        }

        sb.Append("],\"filters\":[");
        for (int i = 0; i < Filters.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append("{\"column\":").Append(Filters[i].Column).Append(",\"text\":");
            AppendString(sb, Filters[i].Text);
            sb.Append('}');
        }

        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>Parses state previously produced by <see cref="ToJson"/>.</summary>
    public static GridState FromJson(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        return new GridState
        {
            ColumnOrder = ReadInts(root, "columnOrder"),
            ColumnWidths = ReadDoubles(root, "columnWidths"),
            HiddenColumns = ReadInts(root, "hiddenColumns"),
            Sort = ReadObjects(root, "sort", e => new GridSortState(
                e.GetProperty("column").GetInt32(),
                e.TryGetProperty("descending", out JsonElement d) && d.GetBoolean())),
            Filters = ReadObjects(root, "filters", e => new GridFilterState(
                e.GetProperty("column").GetInt32(),
                e.TryGetProperty("text", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty)),
        };
    }

    private static void AppendInts(StringBuilder sb, string name, IReadOnlyList<int> values)
    {
        sb.Append('"').Append(name).Append("\":[");
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(values[i]);
        }

        sb.Append(']');
    }

    private static void AppendDoubles(StringBuilder sb, string name, IReadOnlyList<double> values)
    {
        sb.Append('"').Append(name).Append("\":[");
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(values[i].ToString("R", CultureInfo.InvariantCulture));
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

    private static IReadOnlyList<int> ReadInts(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(e => e.GetInt32()).ToArray()
            : Array.Empty<int>();

    private static IReadOnlyList<double> ReadDoubles(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(e => e.GetDouble()).ToArray()
            : Array.Empty<double>();

    private static IReadOnlyList<T> ReadObjects<T>(JsonElement root, string name, Func<JsonElement, T> map) =>
        root.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(map).ToArray()
            : Array.Empty<T>();
}
