using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// An inline message banner with a severity (info / success / warning / error),
/// optional title, and optional dismiss button. Styling is CSS-variable driven.
/// </summary>
public sealed class DxAlert : ComponentBase
{
    [Parameter] public string Severity { get; set; } = "info";

    [Parameter] public string? Title { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public bool Dismissible { get; set; }

    [Parameter] public EventCallback OnDismiss { get; set; }

    [Parameter] public string? Class { get; set; }

    private string Glyph => Severity switch
    {
        "success" => "✓",
        "warning" => "!",
        "error" => "✕",
        _ => "i",
    };

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-alert dx-alert-{Severity} {Class}".TrimEnd());
        builder.AddAttribute(2, "role", Severity == "error" || Severity == "warning" ? "alert" : "status");

        builder.OpenElement(3, "span");
        builder.AddAttribute(4, "class", "dx-alert-icon");
        builder.AddAttribute(5, "aria-hidden", "true");
        builder.AddContent(6, Glyph);
        builder.CloseElement();

        builder.OpenElement(7, "div");
        builder.AddAttribute(8, "class", "dx-alert-body");
        if (!string.IsNullOrEmpty(Title))
        {
            builder.OpenElement(9, "div");
            builder.AddAttribute(10, "class", "dx-alert-title");
            builder.AddContent(11, Title);
            builder.CloseElement();
        }

        builder.AddContent(12, ChildContent);
        builder.CloseElement();

        if (Dismissible)
        {
            builder.OpenElement(13, "button");
            builder.AddAttribute(14, "type", "button");
            builder.AddAttribute(15, "class", "dx-alert-close");
            builder.AddAttribute(16, "aria-label", "Dismiss");
            builder.AddAttribute(17, "onclick", OnDismiss);
            builder.AddContent(18, "✕");
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
