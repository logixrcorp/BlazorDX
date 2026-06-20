using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using BlazorDX.Primitives.Grid;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Server-side grouping: when the data source implements <see cref="IGridGroupDataSource{TRow}"/>
/// and a group column is set, the grid fetches group summaries (key + count + aggregates) up
/// front, renders collapsed headers, and lazily fetches each group's rows — scoped by the group
/// key — only on expand. So grouping and subtotals scale past memory.
/// </summary>
public sealed class RemoteGroupGridTests : TestContext
{
    public RemoteGroupGridTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    // An in-memory dataset answering both group-summary and per-group row queries like a server.
    private sealed class FakeGroupSource(IEnumerable<WidgetRow> rows) : IGridGroupDataSource<WidgetRow>
    {
        private readonly List<WidgetRow> data = rows.ToList();

        public int GroupCalls { get; private set; }
        public int RowCalls { get; private set; }
        public GridGroupRequest? LastGroupRequest { get; private set; }
        public GridDataRequest? LastRowRequest { get; private set; }

        public Task<GridGroupPage> GetGroupsAsync(GridGroupRequest request, CancellationToken ct)
        {
            GroupCalls++;
            LastGroupRequest = request;
            List<GridGroupSummary> groups = Filter(data, request.Filters)
                .GroupBy(r => Text(r, request.GroupColumn), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new GridGroupSummary(
                    g.Key,
                    g.Count(),
                    request.Aggregations
                        .Select(a => new GridGroupAggregateResult(a.Column, a.Kind, Aggregate(g, a)))
                        .ToList()))
                .ToList();
            return Task.FromResult(new GridGroupPage(groups));
        }

        public Task<GridDataPage<WidgetRow>> GetRowsAsync(GridDataRequest request, CancellationToken ct)
        {
            RowCalls++;
            LastRowRequest = request;
            List<WidgetRow> all = Filter(data, request.Filters).ToList();
            List<WidgetRow> window = all.Skip(request.Skip).Take(request.Take).ToList();
            return Task.FromResult(new GridDataPage<WidgetRow>(window, all.Count));
        }

        private static IEnumerable<WidgetRow> Filter(IEnumerable<WidgetRow> rows, IReadOnlyList<GridColumnFilter> filters)
        {
            foreach (GridColumnFilter f in filters)
            {
                rows = rows.Where(r => Text(r, f.Column).Contains(f.Text, StringComparison.OrdinalIgnoreCase));
            }

            return rows;
        }

        private static double Aggregate(IEnumerable<WidgetRow> group, GridAggregateRequest a)
        {
            IEnumerable<double> values = group.Select(r => (double)r.Quantity);
            return a.Kind switch
            {
                GridAggregateKind.Sum => values.Sum(),
                GridAggregateKind.Mean => values.Average(),
                GridAggregateKind.Min => values.Min(),
                GridAggregateKind.Max => values.Max(),
                _ => values.Count(),
            };
        }

        private static string Text(WidgetRow r, int col) =>
            col == 0 ? r.Name : r.Quantity.ToString();
    }

    private static IEnumerable<WidgetRow> Sample() =>
    [
        new() { Name = "Alpha", Quantity = 1 },
        new() { Name = "Alpha", Quantity = 2 },
        new() { Name = "Alpha", Quantity = 3 },
        new() { Name = "Beta", Quantity = 10 },
        new() { Name = "Beta", Quantity = 20 },
    ];

    private IRenderedComponent<DxDataGrid<WidgetRow>> Render(
        FakeGroupSource source, Dictionary<int, GridAggregateKind>? aggregations = null) =>
        RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.GroupByColumn, 0)
            .Add(g => g.Aggregations, aggregations)
            .Add(g => g.DataSource, source));

    [Fact]
    public void Fetches_group_summaries_and_renders_collapsed_headers()
    {
        FakeGroupSource source = new(Sample());
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render(source);

        grid.WaitForAssertion(() =>
        {
            Assert.Equal(1, source.GroupCalls);   // one GROUP BY round-trip
            Assert.Equal(0, source.RowCalls);      // collapsed: no row fetches yet
            Assert.Equal(2, grid.FindAll(".dx-grid-group").Count);
            Assert.Contains("Alpha", grid.Markup);
            Assert.Contains("(3)", grid.Markup);   // server-reported count
            Assert.Contains("(2)", grid.Markup);
            Assert.Empty(grid.FindAll(".dx-grid-row"));   // no data rows while collapsed
            Assert.Equal("5", grid.Find("[role=grid]").GetAttribute("aria-rowcount"));   // sum of counts
        });
    }

    [Fact]
    public void Expanding_a_group_lazily_loads_its_rows_scoped_by_key()
    {
        FakeGroupSource source = new(Sample());
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render(source);

        grid.WaitForState(() => grid.FindAll(".dx-grid-group").Count == 2);
        grid.FindAll(".dx-grid-group")[0].Click();   // expand Alpha

        grid.WaitForAssertion(() =>
        {
            Assert.Equal(1, source.RowCalls);
            // The grid pinned the request to the group: a filter on the grouped column = key.
            Assert.Contains(source.LastRowRequest!.Filters, f => f.Column == 0 && f.Text == "Alpha");
            Assert.Equal(3, grid.FindAll(".dx-grid-row").Count);   // Alpha's three rows
        });
    }

    [Fact]
    public void Collapsing_then_reexpanding_does_not_refetch()
    {
        FakeGroupSource source = new(Sample());
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render(source);
        grid.WaitForState(() => grid.FindAll(".dx-grid-group").Count == 2);

        grid.FindAll(".dx-grid-group")[0].Click();   // expand → fetch
        grid.WaitForState(() => grid.FindAll(".dx-grid-row").Count == 3);
        Assert.Equal(1, source.RowCalls);

        grid.FindAll(".dx-grid-group")[0].Click();   // collapse → rows gone
        grid.WaitForState(() => grid.FindAll(".dx-grid-row").Count == 0);

        grid.FindAll(".dx-grid-group")[0].Click();   // re-expand → served from cache
        grid.WaitForState(() => grid.FindAll(".dx-grid-row").Count == 3);
        Assert.Equal(1, source.RowCalls);             // still one fetch
    }

    [Fact]
    public void Server_aggregates_render_in_the_group_header()
    {
        FakeGroupSource source = new(Sample());
        IRenderedComponent<DxDataGrid<WidgetRow>> grid =
            Render(source, new Dictionary<int, GridAggregateKind> { [1] = GridAggregateKind.Sum });

        grid.WaitForAssertion(() =>
        {
            // The grid asked the server to compute the subtotal.
            Assert.Contains(source.LastGroupRequest!.Aggregations,
                a => a.Column == 1 && a.Kind == GridAggregateKind.Sum);
            // And the server-computed sums render in the headers (Alpha 1+2+3=6, Beta 10+20=30).
            Assert.Contains("Σ 6", grid.Markup);
            Assert.Contains("Σ 30", grid.Markup);
        });
    }

    [Fact]
    public void Sorting_resummarizes_the_groups_server_side()
    {
        FakeGroupSource source = new(Sample());
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render(source);
        grid.WaitForState(() => source.GroupCalls == 1);

        grid.FindAll(".dx-grid-th-label")[1].Click();   // sort by Quantity

        // A sort change re-summarizes the groups server-side (the new order can change rows
        // per group / aggregates), rather than re-sorting an in-memory cache.
        grid.WaitForAssertion(() => Assert.Equal(2, source.GroupCalls));
    }
}
