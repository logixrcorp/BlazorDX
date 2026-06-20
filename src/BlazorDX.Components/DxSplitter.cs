using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Two resizable panes separated by a draggable divider. Resizing uses the same
/// pure-Blazor pointer-overlay technique as the data grid's column resize (a
/// full-window overlay captures pointer move/up so the gesture never loses the thin
/// divider). Arrow keys nudge the divider for keyboard users. Styling is
/// CSS-variable driven (see dx-display.css).
/// </summary>
public sealed class DxSplitter : ComponentBase
{
    private double firstSize;
    private bool resizing;
    private double startPos;
    private double startSize;
    private bool initialized;

    /// <summary>Layout direction: "horizontal" (side by side) or "vertical" (stacked).</summary>
    [Parameter] public string Orientation { get; set; } = "horizontal";

    /// <summary>The first (sized) pane.</summary>
    [Parameter] public RenderFragment? First { get; set; }

    /// <summary>The second (growing) pane.</summary>
    [Parameter] public RenderFragment? Second { get; set; }

    /// <summary>Initial size of the first pane, in pixels.</summary>
    [Parameter] public double InitialSize { get; set; } = 240;

    /// <summary>Minimum size of the first pane, in pixels.</summary>
    [Parameter] public double MinFirst { get; set; } = 80;

    /// <summary>Keyboard nudge step, in pixels.</summary>
    [Parameter] public double Step { get; set; } = 16;

    /// <summary>Extra CSS classes appended to the root.</summary>
    [Parameter] public string? Class { get; set; }

    private bool Horizontal => Orientation != "vertical";

    protected override void OnParametersSet()
    {
        if (!initialized)
        {
            firstSize = InitialSize;
            initialized = true;
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-splitter dx-splitter-{(Horizontal ? "horizontal" : "vertical")} {Class}".TrimEnd());

        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-splitter-pane");
        builder.AddAttribute(4, "style", Inv($"flex:0 0 {firstSize:0.#}px;"));
        builder.AddContent(5, First);
        builder.CloseElement();

        builder.OpenElement(6, "div");
        builder.AddAttribute(7, "class", "dx-splitter-divider");
        builder.AddAttribute(8, "role", "separator");
        builder.AddAttribute(9, "aria-orientation", Horizontal ? "vertical" : "horizontal");
        builder.AddAttribute(10, "tabindex", "0");
        builder.AddAttribute(11, "onpointerdown", EventCallback.Factory.Create<PointerEventArgs>(this, StartResize));
        builder.AddAttribute(12, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDown));
        builder.CloseElement();

        builder.OpenElement(13, "div");
        builder.AddAttribute(14, "class", "dx-splitter-pane dx-splitter-pane-grow");
        builder.AddContent(15, Second);
        builder.CloseElement();

        // While dragging, a full-window overlay captures pointer move/up reliably.
        if (resizing)
        {
            builder.OpenElement(16, "div");
            builder.AddAttribute(17, "class", "dx-splitter-resize-overlay");
            builder.AddAttribute(18, "style", $"cursor:{(Horizontal ? "col-resize" : "row-resize")}");
            builder.AddAttribute(19, "onpointermove", EventCallback.Factory.Create<PointerEventArgs>(this, OnPointerMove));
            builder.AddAttribute(20, "onpointerup", EventCallback.Factory.Create(this, EndResize));
            builder.AddAttribute(21, "onpointerleave", EventCallback.Factory.Create(this, EndResize));
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void StartResize(PointerEventArgs args)
    {
        resizing = true;
        startPos = Horizontal ? args.ClientX : args.ClientY;
        startSize = firstSize;
        StateHasChanged();
    }

    private void OnPointerMove(PointerEventArgs args)
    {
        if (!resizing)
        {
            return;
        }

        double current = Horizontal ? args.ClientX : args.ClientY;
        firstSize = Math.Max(MinFirst, startSize + (current - startPos));
        StateHasChanged();
    }

    private void EndResize()
    {
        resizing = false;
        StateHasChanged();
    }

    private void OnKeyDown(KeyboardEventArgs args)
    {
        double delta = (Horizontal, args.Key) switch
        {
            (true, "ArrowLeft") => -Step,
            (true, "ArrowRight") => Step,
            (false, "ArrowUp") => -Step,
            (false, "ArrowDown") => Step,
            _ => 0,
        };

        if (delta != 0)
        {
            firstSize = Math.Max(MinFirst, firstSize + delta);
            StateHasChanged();
        }
    }

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
