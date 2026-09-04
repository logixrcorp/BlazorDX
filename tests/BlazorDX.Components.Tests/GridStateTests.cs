using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using BlazorDX.Primitives.Grid;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Layout persistence: <see cref="GridState"/> JSON round-trips reflection-free, and the
/// grid captures / restores column order, sort, and filters while raising
/// <c>OnStateChanged</c> on every layout mutation so a host can auto-save.
/// </summary>
public sealed class GridStateTests : TestContext
{
    public GridStateTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    private static List<WidgetRow> Rows() =>
    [
        new() { Name = "Beta", Quantity = 20 },
        new() { Name = "Alpha", Quantity = 10 },
        new() { Name = "Gamma", Quantity = 30 },
    ];

    private IRenderedComponent<DxDataGrid<WidgetRow>> Render(EventCallback<GridState> onChanged = default) =>
        RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.ShowFilterMenu, true)
            .Add(g => g.OnStateChanged, onChanged));

    private static string[] NameColumn(IRenderedComponent<DxDataGrid<WidgetRow>> grid) =>
        grid.FindAll(".dx-grid-row").Select(r => r.QuerySelectorAll(".dx-grid-cell")[0].TextContent).ToArray();

    [Fact]
    public void Json_round_trips_every_facet_of_state()
    {
        GridState original = new()
        {
            ColumnOrder = [1, 0],
            ColumnWidths = [120.5, 80.25],
            HiddenColumns = [1],
            Sort = [new GridSortState(0, Descending: true)],
            Filters = [new GridFilterState(1, "a\"b\\c")],
        };

        GridState restored = GridState.FromJson(original.ToJson());

        Assert.Equal([1, 0], restored.ColumnOrder);
        Assert.Equal([120.5, 80.25], restored.ColumnWidths);
        Assert.Equal([1], restored.HiddenColumns);
        Assert.Single(restored.Sort);
        Assert.Equal(0, restored.Sort[0].Column);
        Assert.True(restored.Sort[0].Descending);
        Assert.Single(restored.Filters);
        Assert.Equal(1, restored.Filters[0].Column);
        Assert.Equal("a\"b\\c", restored.Filters[0].Text);   // quotes/backslashes survive escaping
    }

    [Fact]
    public void FromJson_ignores_missing_sections()
    {
        GridState state = GridState.FromJson("{}");

        Assert.Empty(state.ColumnOrder);
        Assert.Empty(state.Sort);
        Assert.Empty(state.Filters);
    }

    [Fact]
    public void CaptureState_reflects_the_active_sort()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();

        grid.FindAll(".dx-grid-th-label")[0].Click();   // Name ascending

        GridState state = grid.Instance.CaptureState();
        Assert.Single(state.Sort);
        Assert.Equal(0, state.Sort[0].Column);
        Assert.False(state.Sort[0].Descending);
    }

    [Fact]
    public async Task ApplyStateAsync_restores_a_saved_sort()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();
        Assert.Equal(["Beta", "Alpha", "Gamma"], NameColumn(grid));   // unsorted (insertion order)

        GridState saved = new() { Sort = [new GridSortState(0, Descending: false)] };
        await grid.InvokeAsync(() => grid.Instance.ApplyStateAsync(saved));

        Assert.Equal(["Alpha", "Beta", "Gamma"], NameColumn(grid));   // restored sort took effect
    }

    [Fact]
    public async Task ApplyStateAsync_ignores_out_of_range_and_non_permutation_entries()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();

        // 2-column grid: a duplicate-index order and an out-of-range sort must be discarded.
        GridState corrupt = new()
        {
            ColumnOrder = [0, 0],                       // not a permutation
            Sort = [new GridSortState(9, Descending: false)],   // no such column
            HiddenColumns = [42],                       // out of range
        };
        await grid.InvokeAsync(() => grid.Instance.ApplyStateAsync(corrupt));

        GridState after = grid.Instance.CaptureState();
        Assert.Equal([0, 1], after.ColumnOrder);   // original identity order preserved
        Assert.Empty(after.Sort);
        Assert.Empty(after.HiddenColumns);
    }

    [Fact]
    public void Sorting_raises_OnStateChanged_with_the_new_state()
    {
        GridState? captured = null;
        IRenderedComponent<DxDataGrid<WidgetRow>> grid =
            Render(EventCallback.Factory.Create<GridState>(this, s => captured = s));

        grid.FindAll(".dx-grid-th-label")[1].Click();   // Quantity ascending

        Assert.NotNull(captured);
        Assert.Single(captured!.Sort);
        Assert.Equal(1, captured.Sort[0].Column);
    }

    [Fact]
    public async Task Round_trip_through_persistence_restores_the_grid()
    {
        // Simulate the host: sort, serialize to a "store", rebuild, restore from the store.
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();
        grid.FindAll(".dx-grid-th-label")[0].Click();   // Name ascending
        string persisted = grid.Instance.CaptureState().ToJson();

        IRenderedComponent<DxDataGrid<WidgetRow>> fresh = Render();
        Assert.Equal(["Beta", "Alpha", "Gamma"], NameColumn(fresh));   // starts unsorted
        await fresh.InvokeAsync(() => fresh.Instance.ApplyStateAsync(GridState.FromJson(persisted)));

        Assert.Equal(["Alpha", "Beta", "Gamma"], NameColumn(fresh));
    }
}
