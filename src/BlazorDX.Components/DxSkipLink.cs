using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A "skip to main content" link for WCAG 2.4.1 (Bypass Blocks). Render it as the very
/// first focusable element in your layout: it stays visually offscreen until focused,
/// then appears, letting keyboard and screen-reader users jump past repeated navigation
/// straight to the main region.
///
/// Give your main content element a matching id (and <c>tabindex="-1"</c> so it can take
/// focus): <c>&lt;main id="main-content" tabindex="-1"&gt;</c>. Styling is token-driven
/// (see dx-layout.css).
/// </summary>
public sealed class DxSkipLink : ComponentBase
{
    /// <summary>Fragment id of the main-content target (default <c>main-content</c>).</summary>
    [Parameter] public string TargetId { get; set; } = "main-content";

    /// <summary>Link text (default "Skip to main content").</summary>
    [Parameter] public string Text { get; set; } = "Skip to main content";

    /// <summary>Extra CSS classes appended to the link.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "a");
        builder.AddAttribute(1, "class", $"dx-skip-link {Class}".TrimEnd());
        builder.AddAttribute(2, "href", $"#{TargetId}");
        builder.AddContent(3, Text);
        builder.CloseElement();
    }
}
