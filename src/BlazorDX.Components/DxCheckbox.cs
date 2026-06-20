using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A labelled checkbox built on a native <c>&lt;input type="checkbox"&gt;</c> for
/// free accessibility and keyboard handling, with a styled box. Two-way bind via
/// <c>@bind-Value</c>. Styling is CSS-variable driven (see dx-layout.css).
/// </summary>
public sealed class DxCheckbox : ComponentBase
{
    [Parameter] public bool Value { get; set; }

    [Parameter] public EventCallback<bool> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "label");
        builder.AddAttribute(1, "class", Disabled ? $"dx-check dx-check-disabled {Class}".TrimEnd() : $"dx-check {Class}".TrimEnd());

        builder.OpenElement(2, "input");
        builder.AddAttribute(3, "type", "checkbox");
        builder.AddAttribute(4, "class", "dx-check-input");
        builder.AddAttribute(5, "checked", Value);
        builder.AddAttribute(6, "disabled", Disabled);
        builder.AddAttribute(7, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, OnChangeAsync));
        builder.CloseElement();

        builder.OpenElement(8, "span");
        builder.AddAttribute(9, "class", "dx-check-box");
        builder.AddAttribute(10, "aria-hidden", "true");
        builder.CloseElement();

        if (!string.IsNullOrEmpty(Label))
        {
            builder.OpenElement(11, "span");
            builder.AddAttribute(12, "class", "dx-check-label");
            builder.AddContent(13, Label);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private Task OnChangeAsync(ChangeEventArgs args)
    {
        bool isChecked = args.Value is bool value && value;
        return ValueChanged.HasDelegate ? ValueChanged.InvokeAsync(isChecked) : Task.CompletedTask;
    }
}
