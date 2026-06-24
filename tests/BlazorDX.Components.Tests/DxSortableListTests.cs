using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + drag/keyboard reordering for the styled sortable list.</summary>
public sealed class DxSortableListTests : TestContext
{
    private static IReadOnlyList<string> Tasks() => ["A", "B", "C"];

    [Fact]
    public void Renders_draggable_items()
    {
        IRenderedComponent<DxSortableList> list = RenderComponent<DxSortableList>(parameters => parameters
            .Add(s => s.Items, Tasks()));

        var items = list.FindAll("[role=listitem]");
        Assert.Equal(3, items.Count);
        Assert.Equal("true", items[0].GetAttribute("draggable"));
    }

    [Fact]
    public void Dragging_an_item_onto_another_reorders()
    {
        IReadOnlyList<string> bound = Tasks();
        IRenderedComponent<DxSortableList> list = RenderComponent<DxSortableList>(parameters => parameters
            .Add(s => s.Items, bound)
            .Add(s => s.ItemsChanged, items => bound = items));

        // Re-query between events: each event triggers a re-render.
        list.FindAll("[role=listitem]")[0].TriggerEvent("ondragstart", new DragEventArgs()); // grab "A"
        list.FindAll("[role=listitem]")[2].TriggerEvent("ondrop", new DragEventArgs());       // drop on "C"

        Assert.Equal(["B", "C", "A"], bound);
    }

    [Fact]
    public void Wires_dragover_preventDefault_so_drops_are_accepted()
    {
        // Regression: drop-accept must use AddEventPreventDefaultAttribute, which Blazor
        // emits as the prefixed "blazor:ondragover:preventDefault" directive. The earlier
        // bug used a plain AddAttribute("ondragover:preventDefault", true) — a literal,
        // unprefixed HTML attribute that is a silent no-op and breaks HTML5 drop in the
        // browser (the "denied" cursor).
        IRenderedComponent<DxSortableList> list = RenderComponent<DxSortableList>(parameters => parameters
            .Add(s => s.Items, Tasks()));

        Assert.Contains("blazor:ondragover:preventDefault", list.Markup);
    }

    [Fact]
    public void Move_down_button_reorders_with_a_single_click()
    {
        // WCAG 2.5.7: a no-drag, single-pointer alternative to the drag gesture.
        IReadOnlyList<string> bound = Tasks();
        IRenderedComponent<DxSortableList> list = RenderComponent<DxSortableList>(parameters => parameters
            .Add(s => s.Items, bound)
            .Add(s => s.ItemsChanged, items => bound = items));

        list.Find(".dx-sortable-move[aria-label='Move A down']").Click();

        Assert.Equal(["B", "A", "C"], bound);
    }

    [Fact]
    public void Move_buttons_are_disabled_at_the_list_ends()
    {
        IRenderedComponent<DxSortableList> list = RenderComponent<DxSortableList>(parameters => parameters
            .Add(s => s.Items, Tasks()));

        Assert.True(list.Find(".dx-sortable-move[aria-label='Move A up']").HasAttribute("disabled"));
        Assert.True(list.Find(".dx-sortable-move[aria-label='Move C down']").HasAttribute("disabled"));
    }

    [Fact]
    public void Alt_arrow_down_moves_the_focused_item_down()
    {
        IReadOnlyList<string> bound = Tasks();
        IRenderedComponent<DxSortableList> list = RenderComponent<DxSortableList>(parameters => parameters
            .Add(s => s.Items, bound)
            .Add(s => s.ItemsChanged, items => bound = items));

        list.FindAll("[role=listitem]")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        Assert.Equal(["B", "A", "C"], bound);
    }
}
