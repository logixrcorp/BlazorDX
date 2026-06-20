using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Scopes a theme over its children by setting <c>data-dx-theme</c> (light/dark)
/// and optional inline token overrides. Everything inside inherits the tokens via
/// CSS custom properties — no per-component wiring. Requires dx-theme.css.
/// </summary>
public sealed class DxThemeProvider : ComponentBase
{
    /// <summary>"light" or "dark" (or any value matching a [data-dx-theme] rule).</summary>
    [Parameter] public string Theme { get; set; } = "light";

    /// <summary>Optional override for the accent token (any CSS color).</summary>
    [Parameter] public string? Accent { get; set; }

    /// <summary>Text direction for the subtree: "ltr" (default) or "rtl".</summary>
    [Parameter] public string Direction { get; set; } = "ltr";

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-theme-root {Class}".TrimEnd());
        builder.AddAttribute(2, "data-dx-theme", Theme);
        builder.AddAttribute(3, "dir", Direction);
        if (!string.IsNullOrEmpty(Accent))
        {
            builder.AddAttribute(4, "style", $"--dx-accent:{Accent};");
        }

        builder.AddContent(5, ChildContent);
        builder.CloseElement();
    }
}
