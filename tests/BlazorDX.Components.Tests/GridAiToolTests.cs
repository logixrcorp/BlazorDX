using System.Text.Json;
using System.Threading.Tasks;
using BlazorDX.Primitives.Forms;
using BlazorDX.Primitives.Grid;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// A server-side grid data source exposed to an assistant as a read-only tool: the model filters,
/// sorts and pages the same source the grid binds to.
/// </summary>
/// <remarks>
/// The schema and the reply are hand-built strings (zero-reflection, AOT-safe, per ADR 0002), so
/// several of these tests parse the output rather than matching substrings. A stray comma or an
/// unbalanced brace in a StringBuilder is exactly the defect this component can have, and it is
/// one a substring assertion would sail straight past.
/// </remarks>
public sealed class GridAiToolTests
{
    // Recording source: the point of most of these tests is what reached the data source, not what
    // came back, because translating the model's request is where this tool can be wrong.
    private sealed class RecordingSource : IGridDataSource<WidgetRow>
    {
        public GridDataRequest? Last { get; private set; }

        public List<WidgetRow> Rows { get; init; } =
        [
            new() { Name = "Alpha", Quantity = 10 },
            new() { Name = "Beta", Quantity = 20 },
        ];

        public int TotalCount { get; init; } = 2;

        public Task<GridDataPage<WidgetRow>> GetRowsAsync(GridDataRequest request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(new GridDataPage<WidgetRow>(Rows, TotalCount));
        }
    }

    private static GridAiTool<WidgetRow> NewTool(RecordingSource source, int maxRows = 50) =>
        new("query_widgets", "Widget inventory.", new WidgetRowGridAccessor(), source, maxRows);

    [Fact]
    public void The_tool_is_read_only()
    {
        // Not a constructor flag: IGridDataSource has no write path, so a writable grid tool is a
        // thing someone has to build deliberately rather than switch on here by accident.
        Assert.True(NewTool(new RecordingSource()).IsReadOnly);
    }

    [Fact]
    public void The_schema_is_valid_json_and_names_the_real_columns()
    {
        string schema = NewTool(new RecordingSource()).InputSchemaJson;

        using JsonDocument doc = JsonDocument.Parse(schema);
        JsonElement properties = doc.RootElement.GetProperty("properties");

        // The column is an enum, not a free-text field name. A model cannot invent a column, and a
        // wrong guess is a schema violation rather than a query that silently matches nothing.
        JsonElement columnEnum = properties
            .GetProperty("filters").GetProperty("items")
            .GetProperty("properties").GetProperty("column").GetProperty("enum");

        string[] columns = [.. columnEnum.EnumerateArray().Select(e => e.GetString()!)];
        Assert.Equal(["Name", "Quantity"], columns);

        Assert.Equal(4, properties.EnumerateObject().Count());
        Assert.Equal(50, properties.GetProperty("take").GetProperty("maximum").GetInt32());
    }

    [Fact]
    public async Task Filters_and_sorts_are_translated_into_a_grid_request()
    {
        RecordingSource source = new();

        await NewTool(source).InvokeAsync(
            """{"filters":[{"column":"Name","contains":"al"}],"sort":[{"column":"Quantity","descending":true}]}""", CancellationToken.None);

        GridDataRequest request = Assert.IsType<GridDataRequest>(source.Last);

        // Both the index and the header travel: a data source maps whichever it prefers to its own
        // query, and the index is meaningless without the header agreeing with it.
        GridColumnFilter filter = Assert.Single(request.Filters);
        Assert.Equal(0, filter.Column);
        Assert.Equal("Name", filter.Field);
        Assert.Equal("al", filter.Text);

        GridSortKey sort = Assert.Single(request.Sort);
        Assert.Equal(1, sort.Column);
        Assert.Equal("Quantity", sort.Field);
        Assert.True(sort.Descending);
    }

    [Fact]
    public async Task An_unknown_column_is_an_error_that_names_the_real_ones()
    {
        AiToolResult result = await NewTool(new RecordingSource()).InvokeAsync(
            """{"filters":[{"column":"Colour","contains":"red"}]}""", CancellationToken.None);

        // Answering "unknown column" alone would leave the model guessing again. Listing them lets
        // it correct itself on the next call.
        Assert.True(result.IsError);
        Assert.Contains("Colour", result.Text);
        Assert.Contains("Name, Quantity", result.Text);
    }

    [Fact]
    public async Task Take_is_clamped_rather_than_rejected()
    {
        RecordingSource source = new();

        await NewTool(source, maxRows: 5).InvokeAsync(
            """{"take":1000}""", CancellationToken.None);

        // A model asking for everything wants as much as it can have; the totalCount in the reply
        // is what tells it the answer is partial.
        Assert.Equal(5, source.Last!.Take);
    }

    [Fact]
    public async Task The_reply_reports_the_full_count_even_when_the_page_is_smaller()
    {
        RecordingSource source = new() { TotalCount = 4210 };

        AiToolResult result = await NewTool(source).InvokeAsync(
            """{"take":2}""", CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(result.Text);
        Assert.Equal(4210, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task Numeric_columns_come_back_as_numbers_and_text_columns_as_strings()
    {
        AiToolResult result = await NewTool(new RecordingSource()).InvokeAsync(
            "{}", CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(result.Text);
        JsonElement first = doc.RootElement.GetProperty("rows")[0];

        // So a model can total or compare a column without re-parsing text it was just handed.
        Assert.Equal(JsonValueKind.String, first.GetProperty("Name").ValueKind);
        Assert.Equal(JsonValueKind.Number, first.GetProperty("Quantity").ValueKind);
        Assert.Equal(10, first.GetProperty("Quantity").GetInt32());
    }

    [Fact]
    public async Task Absent_and_empty_arguments_both_mean_the_default_page()
    {
        RecordingSource source = new();

        await NewTool(source).InvokeAsync("{}", CancellationToken.None);
        Assert.Equal(0, source.Last!.Skip);
        Assert.Empty(source.Last.Filters);

        // An assistant that sends nothing at all should not be an error either.
        await NewTool(source).InvokeAsync(string.Empty, CancellationToken.None);
        Assert.Equal(0, source.Last!.Skip);
    }

    [Fact]
    public async Task Malformed_arguments_are_reported_rather_than_thrown()
    {
        // The model is the caller here, so a parse failure has to come back as something it can
        // read and retry from — an exception would surface as a transport error instead.
        AiToolResult result = await NewTool(new RecordingSource()).InvokeAsync(
            "{not json", CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("not valid JSON", result.Text);
    }

    [Fact]
    public async Task A_wrongly_typed_argument_is_an_error_rather_than_an_exception()
    {
        // JsonElement.GetString() and TryGetInt32() both throw on a mismatched kind, and the
        // caller here is a language model, which does send {"column": 3}. Escaping as an
        // exception would surface at the transport as a dead call rather than something the
        // model could read and correct, so every read is kind-checked first.
        AiToolResult result = await NewTool(new RecordingSource()).InvokeAsync(
            """{"filters":[{"column":3,"contains":"x"}]}""", CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Name, Quantity", result.Text);
    }

    [Fact]
    public async Task A_string_where_a_number_belongs_falls_back_to_the_default()
    {
        RecordingSource source = new();

        // {"take":"5"} used to throw out of TryGetInt32. Ignoring the bad value and answering the
        // default page is the recoverable behaviour: the reply's totalCount still tells the model
        // what it did not see, so it can ask again properly.
        await NewTool(source).InvokeAsync("""{"take":"5","skip":"2"}""", CancellationToken.None);

        Assert.Equal(20, source.Last!.Take);
        Assert.Equal(0, source.Last.Skip);
    }

    [Fact]
    public async Task A_numeric_filter_value_is_matched_as_its_text()
    {
        RecordingSource source = new();

        // A model filtering a numeric column naturally sends a number, not a string. Dropping it
        // would silently widen the query to every row, which is worse than either erroring or
        // matching — so the raw text is used.
        await NewTool(source).InvokeAsync(
            """{"filters":[{"column":"Quantity","contains":20}]}""", CancellationToken.None);

        Assert.Equal("20", Assert.Single(source.Last!.Filters).Text);
    }

    [Fact]
    public async Task Cell_text_is_escaped_so_the_reply_stays_parseable()
    {
        RecordingSource source = new()
        {
            Rows = [new WidgetRow { Name = "He said \"hi\"\n\tand left", Quantity = 1 }],
            TotalCount = 1,
        };

        AiToolResult result = await NewTool(source).InvokeAsync("{}", CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(result.Text);
        Assert.Equal("He said \"hi\"\n\tand left", doc.RootElement.GetProperty("rows")[0].GetProperty("Name").GetString());
    }
}
