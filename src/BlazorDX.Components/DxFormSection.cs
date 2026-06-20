using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Groups related form fields into a labelled, optionally collapsible
/// <c>&lt;fieldset&gt;</c> — the "containerization within forms" that keeps long forms
/// scannable. Drop <see cref="DxFormField"/>s (or any markup) inside it.
/// </summary>
public sealed class DxFormSection : ComponentBase
{
    /// <summary>Section heading (the fieldset legend).</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Optional helper text under the legend.</summary>
    [Parameter] public string? Description { get; set; }

    /// <summary>Allow the section to collapse/expand.</summary>
    [Parameter] public bool Collapsible { get; set; }

    /// <summary>Initial/!two-way collapsed state.</summary>
    [Parameter] public bool Collapsed { get; set; }

    /// <summary>The section's fields/content.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Extra CSS classes appended to the section.</summary>
    [Parameter] public string? Class { get; set; }

    private bool collapsed;

    protected override void OnParametersSet() => collapsed = Collapsed;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "fieldset");
        builder.AddAttribute(1, "class", $"dx-form-section {Class}".TrimEnd());

        if (!string.IsNullOrEmpty(Title))
        {
            builder.OpenElement(2, "legend");
            builder.AddAttribute(3, "class", "dx-form-section-legend");

            if (Collapsible)
            {
                builder.OpenElement(4, "button");
                builder.AddAttribute(5, "type", "button");
                builder.AddAttribute(6, "class", "dx-form-section-toggle");
                builder.AddAttribute(7, "aria-expanded", collapsed ? "false" : "true");
                builder.AddAttribute(8, "onclick", EventCallback.Factory.Create(this, () => collapsed = !collapsed));
                builder.OpenElement(9, "span");
                builder.AddAttribute(10, "class", "dx-form-section-caret");
                builder.AddAttribute(11, "aria-hidden", "true");
                builder.AddContent(12, collapsed ? "▸" : "▾");
                builder.CloseElement();
                builder.AddContent(13, Title);
                builder.CloseElement();
            }
            else
            {
                builder.AddContent(14, Title);
            }

            builder.CloseElement();
        }

        if (!collapsed)
        {
            if (!string.IsNullOrEmpty(Description))
            {
                builder.OpenElement(15, "p");
                builder.AddAttribute(16, "class", "dx-form-section-desc");
                builder.AddContent(17, Description);
                builder.CloseElement();
            }

            builder.OpenElement(18, "div");
            builder.AddAttribute(19, "class", "dx-form-section-body");
            builder.AddContent(20, ChildContent);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
