using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A scroll-driven narrative section wrapping a sequence of <see cref="DxEditorialScrollyStage"/>
/// children. The reveal is pure CSS transitions (dx-editorial.css's reveal class), toggled by
/// an IntersectionObserver-only script — never a scroll-position listener, so nothing runs per
/// scroll pixel. Add
/// <c>&lt;script type="module" src="_content/BlazorDX.Components/dx-editorial-scrollytelling.js"&gt;&lt;/script&gt;</c>
/// once to your app (alongside the dx-editorial.css link) to enable the reveal. Without it,
/// stages render fully visible under the <c>(scripting: none)</c> CSS guard — but if scripting
/// is enabled and the script tag is simply omitted, that guard won't apply, so stages stay
/// hidden. The script is required, not optional, for the reveal to work.
/// </summary>
public sealed class DxEditorialScrollytelling : ComponentBase
{
    [Parameter] public string? Intro { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "section");
        builder.AddAttribute(1, "class", "dx-editorial-scrolly");

        if (!string.IsNullOrEmpty(Intro))
        {
            builder.OpenElement(2, "p");
            builder.AddAttribute(3, "class", "dx-editorial-scrolly-intro");
            builder.AddContent(4, Intro);
            builder.CloseElement();
        }

        builder.AddContent(5, ChildContent);

        builder.CloseElement();
    }
}
