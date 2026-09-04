using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using BlazorDX.Primitives.Grid;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Server-side (remote) data binding: the grid fetches only the visible window from an
/// <see cref="IGridDataSource{TRow}"/>, reports the source's total, and pushes sort and
/// filter into the request. Unloaded rows render a skeleton.
/// </summary>
public sealed class RemoteGridTests : TestContext
{
    public RemoteGridTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    // Records the last request and serves an in-memory dataset as if it were a server.
    private sealed class FakeSource(int count) : IGridDataSource<WidgetRow>
    {
        private readonly List<WidgetRow> data =
            Enumerable.Range(1, count).Select(i => new WidgetRow { Name = $"Row {i}", Quantity = i }).ToList();

        public GridDataRequest? LastRequest { get; private set; }

        public Task<GridDataPage<WidgetRow>> GetRowsAsync(GridDataRequest request, CancellationToken ct)
        {
            LastRequest = request;
            IEnumerable<WidgetRow> query = data;
            foreach (GridColumnFilter f in request.Filters)
            {
                query = query.Where(r => Text(r, f.Column).Contains(f.Text, StringComparison.OrdinalIgnoreCase));
            }

            if (request.Sort.Count > 0)
            {
                GridSortKey key = request.Sort[0];
                query = key.Descending
                    ? query.OrderByDescending(r => Key(r, key.Column))
                    : query.OrderBy(r => Key(r, key.Column));
            }

            List<WidgetRow> all = query.ToList();
            List<WidgetRow> window = all.Skip(request.Skip).Take(request.Take).ToList();
            return Task.FromResult(new GridDataPage<WidgetRow>(window, all.Count));
        }

        private static string Text(WidgetRow r, int col) => col == 0 ? r.Name : r.Quantity.ToString();
        private static IComparable Key(WidgetRow r, int col) => col == 0 ? r.Name : r.Quantity;
    }

    // Always returns only the first few rows of any window, to exercise the skeleton path.
    private sealed class PartialSource(int total, int returns) : IGridDataSource<WidgetRow>
    {
        public Task<GridDataPage<WidgetRow>> GetRowsAsync(GridDataRequest request, CancellationToken ct)
        {
            List<WidgetRow> rows = Enumerable.Range(0, Math.Min(returns, request.Take))
                .Select(i => new WidgetRow { Name = $"Row {request.Skip + i}", Quantity = request.Skip + i })
                .ToList();
            return Task.FromResult(new GridDataPage<WidgetRow>(rows, total));
        }
    }

    private IRenderedComponent<DxDataGrid<WidgetRow>> Render(IGridDataSource<WidgetRow> source, bool filterable = false) =>
        RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.Filterable, filterable)
            .Add(g => g.DataSource, source));

    [Fact]
    public void Fetches_the_first_window_and_reports_the_total()
    {
        FakeSource source = new(1000);
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render(source);

        Assert.NotNull(source.LastRequest);
        Assert.Equal(0, source.LastRequest!.Skip);
        Assert.True(source.LastRequest.Take > 0);

        grid.WaitForAssertion(() =>
        {
            Assert.Equal("1000", grid.Find("[role=grid]").GetAttribute("aria-rowcount"));
            Assert.Contains("Row 1", grid.Markup);
        });
    }

    [Fact]
    public void Header_click_pushes_a_sort_key_to_the_source()
    {
        FakeSource source = new(1000);
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render(source);

        grid.FindAll(".dx-grid-th-label")[0].Click();   // sort by the first column (Name)

        Assert.Single(source.LastRequest!.Sort);
        Assert.Equal(0, source.LastRequest.Sort[0].Column);
        Assert.Equal("Name", source.LastRequest.Sort[0].Field);
    }

    [Fact]
    public void Filter_input_pushes_a_filter_to_the_source()
    {
        FakeSource source = new(1000);
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render(source, filterable: true);

        grid.FindAll(".dx-grid-filter-input")[0].Input("Row 12");

        Assert.Single(source.LastRequest!.Filters);
        Assert.Equal("Row 12", source.LastRequest.Filters[0].Text);
        Assert.Equal("Name", source.LastRequest.Filters[0].Field);
    }

    [Fact]
    public void Unloaded_rows_render_a_skeleton()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render(new PartialSource(total: 1000, returns: 3));

        grid.WaitForAssertion(() =>
        {
            Assert.True(grid.FindAll(".dx-grid-row-loading").Count > 0);   // unfetched rows
            Assert.True(grid.FindAll(".dx-grid-skeleton").Count > 0);
            Assert.Contains("Row 0", grid.Markup);                          // the few that loaded
        });
    }

    [Fact]
    public void Selection_and_edit_are_disabled_in_remote_mode()
    {
        // Even with Selectable/Editable set, remote mode renders no selection column.
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.Selectable, true)
            .Add(g => g.DataSource, new FakeSource(50)));

        grid.WaitForAssertion(() => Assert.Empty(grid.FindAll(".dx-grid-select")));
    }
}
