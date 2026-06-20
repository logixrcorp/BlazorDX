using BlazorDX.Primitives.Interaction;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled tile dashboard built on <see cref="TileLayoutPrimitive"/>. Tiles
/// lay out in a responsive CSS grid; each tile's header is a drag handle that also
/// supports keyboard reordering (Alt+Arrow). Styling is token-driven (see
/// dx-layout.css).
/// </summary>
public sealed class DxTileLayout : TileLayoutPrimitive
{
    /// <summary>Minimum tile column width before the grid wraps (px).</summary>
    [Parameter] public int MinColumnWidth { get; set; } = 220;

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-tiles {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "list");
        builder.AddAttribute(3, "style",
            $"display:grid;gap:14px;grid-template-columns:repeat(auto-fill,minmax({MinColumnWidth}px,1fr));");

        for (int position = 0; position < TileCount; position++)
        {
            BuildTile(builder, position);
        }

        builder.CloseElement();
    }

    private void BuildTile(RenderTreeBuilder builder, int position)
    {
        TileItem tile = TileAt(position);
        int captured = position;

        builder.OpenElement(4, "div");
        builder.SetKey(Order[position]);   // key by tile identity, not position
        builder.AddAttribute(5, "class", "dx-tile");
        builder.AddAttribute(6, "role", "listitem");
        builder.AddAttribute(7, "style", $"grid-column:span {Math.Max(1, tile.ColumnSpan)};");
        builder.AddAttribute(8, "ondragover", EventCallback.Factory.Create(this, () => { }));
        builder.AddEventPreventDefaultAttribute(9, "ondragover", true);
        builder.AddAttribute(10, "ondrop", EventCallback.Factory.Create(this, () => OnDropAsync(captured)));

        // Header doubles as the drag handle and the keyboard reorder target.
        builder.OpenElement(11, "div");
        builder.AddAttribute(12, "class", "dx-tile-header");
        builder.AddAttribute(13, "draggable", "true");
        builder.AddAttribute(14, "tabindex", IsActive(position) ? "0" : "-1");
        builder.AddAttribute(15, "title", "Drag, or Alt+Arrow, to reorder");
        builder.AddAttribute(16, "aria-label", $"{tile.Title} — drag or Alt+Arrow to reorder");
        builder.AddAttribute(17, "ondragstart", EventCallback.Factory.Create(this, () => OnDragStart(captured)));
        builder.AddAttribute(18, "onkeydown",
            EventCallback.Factory.Create<KeyboardEventArgs>(this, e => OnKeyDownAsync(e, captured)));
        builder.AddElementReferenceCapture(19, element => CaptureHandle(captured, element));

        builder.OpenElement(20, "span");
        builder.AddAttribute(21, "class", "dx-tile-grip");
        builder.AddAttribute(22, "aria-hidden", "true");
        builder.AddContent(23, "⠿");
        builder.CloseElement();

        builder.OpenElement(24, "span");
        builder.AddAttribute(25, "class", "dx-tile-title");
        builder.AddContent(26, tile.Title);
        builder.CloseElement();

        builder.CloseElement();   // header

        builder.OpenElement(27, "div");
        builder.AddAttribute(28, "class", "dx-tile-body");
        builder.AddContent(29, tile.Content);
        builder.CloseElement();

        builder.CloseElement();   // tile
    }
}
