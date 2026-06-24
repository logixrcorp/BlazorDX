using BlazorDX.Components;
using BlazorDX.Primitives.Interaction;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Tile dashboard rendering + drag/keyboard reordering.</summary>
public sealed class DxTileLayoutTests : TestContext
{
    private static IReadOnlyList<TileItem> Tiles() =>
    [
        new TileItem("Alpha", b => b.AddContent(0, "a")),
        new TileItem("Beta", b => b.AddContent(0, "b")),
        new TileItem("Gamma", b => b.AddContent(0, "c"), 2),
    ];

    private static string[] Titles(IRenderedComponent<DxTileLayout> c) =>
        c.FindAll(".dx-tile-title").Select(e => e.TextContent).ToArray();

    [Fact]
    public void Renders_a_tile_per_item_with_header_and_body()
    {
        IRenderedComponent<DxTileLayout> tiles = RenderComponent<DxTileLayout>(parameters => parameters
            .Add(t => t.Tiles, Tiles()));

        Assert.Equal(3, tiles.FindAll(".dx-tile").Count);
        Assert.Equal(["Alpha", "Beta", "Gamma"], Titles(tiles));
        Assert.Contains("a", tiles.FindAll(".dx-tile-body")[0].TextContent);
    }

    [Fact]
    public void Column_span_is_applied_to_the_tile()
    {
        IRenderedComponent<DxTileLayout> tiles = RenderComponent<DxTileLayout>(parameters => parameters
            .Add(t => t.Tiles, Tiles()));

        Assert.Contains("span 2", tiles.FindAll(".dx-tile")[2].GetAttribute("style")!);
    }

    [Fact]
    public void Dragging_a_header_onto_another_reorders_and_raises_order()
    {
        IReadOnlyList<int>? order = null;
        IRenderedComponent<DxTileLayout> tiles = RenderComponent<DxTileLayout>(parameters => parameters
            .Add(t => t.Tiles, Tiles())
            .Add(t => t.OrderChanged, o => order = o));

        // Drag tile 0 (Alpha) and drop on tile 2 (Gamma).
        tiles.FindAll(".dx-tile-header")[0].TriggerEvent("ondragstart", new DragEventArgs());
        tiles.FindAll(".dx-tile")[2].TriggerEvent("ondrop", new DragEventArgs());

        Assert.Equal(["Beta", "Gamma", "Alpha"], Titles(tiles));
        Assert.NotNull(order);
        Assert.Equal([1, 2, 0], order!);
    }

    [Fact]
    public void Alt_arrow_right_moves_the_focused_tile_later()
    {
        IRenderedComponent<DxTileLayout> tiles = RenderComponent<DxTileLayout>(parameters => parameters
            .Add(t => t.Tiles, Tiles()));

        tiles.FindAll(".dx-tile-header")[0]
            .KeyDown(new KeyboardEventArgs { Key = "ArrowRight", AltKey = true });

        Assert.Equal(["Beta", "Alpha", "Gamma"], Titles(tiles));
    }

    [Fact]
    public void Move_later_button_reorders_with_a_single_click()
    {
        // WCAG 2.5.7: a no-drag, single-pointer alternative to dragging the header.
        IReadOnlyList<int>? order = null;
        IRenderedComponent<DxTileLayout> tiles = RenderComponent<DxTileLayout>(parameters => parameters
            .Add(t => t.Tiles, Tiles())
            .Add(t => t.OrderChanged, o => order = o));

        tiles.Find(".dx-tile-move[aria-label='Move Alpha later']").Click();

        Assert.Equal(["Beta", "Alpha", "Gamma"], Titles(tiles));
        Assert.Equal([1, 0, 2], order!);
    }

    [Fact]
    public void Move_buttons_are_disabled_at_the_ends()
    {
        IRenderedComponent<DxTileLayout> tiles = RenderComponent<DxTileLayout>(parameters => parameters
            .Add(t => t.Tiles, Tiles()));

        Assert.True(tiles.Find(".dx-tile-move[aria-label='Move Alpha earlier']").HasAttribute("disabled"));
        Assert.True(tiles.Find(".dx-tile-move[aria-label='Move Gamma later']").HasAttribute("disabled"));
    }

    [Fact]
    public void Arrow_alone_moves_focus_not_order()
    {
        IRenderedComponent<DxTileLayout> tiles = RenderComponent<DxTileLayout>(parameters => parameters
            .Add(t => t.Tiles, Tiles()));

        tiles.FindAll(".dx-tile-header")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        // Order unchanged; focus tab-stop moved to the second header.
        Assert.Equal(["Alpha", "Beta", "Gamma"], Titles(tiles));
        Assert.Equal("0", tiles.FindAll(".dx-tile-header")[1].GetAttribute("tabindex"));
        Assert.Equal("-1", tiles.FindAll(".dx-tile-header")[0].GetAttribute("tabindex"));
    }
}
