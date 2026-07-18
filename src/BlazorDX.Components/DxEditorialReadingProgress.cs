using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A thin fixed bar across the top of the viewport that fills as the reader scrolls through a
/// <see cref="DxEditorialLayout"/> piece. Pure CSS via <c>animation-timeline: scroll(root)</c> —
/// the scroll-driven-animations API, the same family as <see cref="DxEditorialFigure"/>'s
/// reveal — so there is no scroll-position listener and nothing runs per scroll frame. Because
/// the fill is tied 1:1 to the reader's own scroll input rather than auto-playing, it's exempt
/// from <c>prefers-reduced-motion</c> concerns (unlike the hero's Ken Burns zoom). Renders inert
/// (0-width, invisible) in browsers without scroll-driven-animation support — never misleading,
/// just absent. Purely decorative, so it's <c>aria-hidden</c>.
/// </summary>
public sealed class DxEditorialReadingProgress : ComponentBase
{
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-editorial-reading-progress {Class}".TrimEnd());
        builder.AddAttribute(2, "aria-hidden", "true");
        builder.CloseElement();
    }
}
