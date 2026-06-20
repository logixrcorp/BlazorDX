using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A surface container with optional header and footer regions. A leaf component;
/// styling is CSS-variable driven (see dx-display.css).
/// </summary>
public sealed class DxCard : ComponentBase
{
    /// <summary>Optional header region.</summary>
    [Parameter] public RenderFragment? Header { get; set; }

    /// <summary>Card body.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Optional footer region.</summary>
    [Parameter] public RenderFragment? Footer { get; set; }

    /// <summary>Extra CSS classes appended to the card.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-card {Class}".TrimEnd());

        if (Header is not null)
        {
            builder.OpenElement(2, "div");
            builder.AddAttribute(3, "class", "dx-card-header");
            builder.AddContent(4, Header);
            builder.CloseElement();
        }

        builder.OpenElement(5, "div");
        builder.AddAttribute(6, "class", "dx-card-body");
        builder.AddContent(7, ChildContent);
        builder.CloseElement();

        if (Footer is not null)
        {
            builder.OpenElement(8, "div");
            builder.AddAttribute(9, "class", "dx-card-footer");
            builder.AddContent(10, Footer);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
