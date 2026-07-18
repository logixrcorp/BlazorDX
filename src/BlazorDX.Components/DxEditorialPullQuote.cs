using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>A large, italicized, serif-accented pull-quote to punctuate a long-form piece.</summary>
public sealed class DxEditorialPullQuote : ComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string? Attribution { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-editorial-pullquote");

        builder.OpenElement(2, "blockquote");
        builder.AddContent(3, ChildContent);
        builder.CloseElement();

        if (!string.IsNullOrEmpty(Attribution))
        {
            builder.OpenElement(4, "cite");
            builder.AddContent(5, Attribution);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
