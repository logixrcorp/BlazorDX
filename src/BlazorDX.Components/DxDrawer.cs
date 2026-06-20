using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A persistent, collapsible side panel that pushes content rather than overlaying
/// it — the non-modal counterpart to <see cref="DxSheet"/>. No backdrop, no focus
/// trap, no scroll lock: the panel simply collapses its width (a CSS transition)
/// and the main content reflows. Use it for app sidebars and navigation rails;
/// use <see cref="DxSheet"/> when you need a modal overlay. Two-way bind via
/// <c>@bind-Open</c>. Styling is token-driven (see dx-structure.css).
/// </summary>
public sealed class DxDrawer : ComponentBase
{
    [Parameter] public bool Open { get; set; } = true;

    [Parameter] public EventCallback<bool> OpenChanged { get; set; }

    /// <summary>Edge the panel docks to: "left" (default) or "right".</summary>
    [Parameter] public string Side { get; set; } = "left";

    /// <summary>The collapsible side panel content.</summary>
    [Parameter] public RenderFragment? Panel { get; set; }

    /// <summary>The main content the panel sits beside.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string AriaLabel { get; set; } = "Side panel";

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        string state = Open ? "dx-drawer-open" : "dx-drawer-closed";
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-drawer dx-drawer-{Side} {state} {Class}".TrimEnd());

        builder.OpenElement(2, "aside");
        builder.AddAttribute(3, "class", "dx-drawer-panel");
        builder.AddAttribute(4, "role", "complementary");
        builder.AddAttribute(5, "aria-label", AriaLabel);
        builder.AddAttribute(6, "aria-hidden", Open ? "false" : "true");
        builder.OpenElement(7, "div");
        builder.AddAttribute(8, "class", "dx-drawer-panel-inner");
        builder.AddContent(9, Panel);
        builder.CloseElement();
        builder.CloseElement();

        builder.OpenElement(10, "div");
        builder.AddAttribute(11, "class", "dx-drawer-content");
        builder.AddContent(12, ChildContent);
        builder.CloseElement();

        builder.CloseElement();
    }
}
