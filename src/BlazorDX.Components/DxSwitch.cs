using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A toggle switch built on a native checkbox with <c>role="switch"</c>, styled as
/// a track + thumb. Two-way bind via <c>@bind-Value</c>. Styling is CSS-variable
/// driven (see dx-layout.css).
/// </summary>
public sealed class DxSwitch : ComponentBase
{
    [Parameter] public bool Value { get; set; }

    [Parameter] public EventCallback<bool> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "label");
        builder.AddAttribute(1, "class", Disabled ? $"dx-switch dx-switch-disabled {Class}".TrimEnd() : $"dx-switch {Class}".TrimEnd());

        builder.OpenElement(2, "input");
        builder.AddAttribute(3, "type", "checkbox");
        builder.AddAttribute(4, "role", "switch");
        builder.AddAttribute(5, "class", "dx-switch-input");
        builder.AddAttribute(6, "checked", Value);
        builder.AddAttribute(7, "aria-checked", Value ? "true" : "false");
        builder.AddAttribute(8, "disabled", Disabled);
        builder.AddAttribute(9, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, OnChangeAsync));
        builder.CloseElement();

        builder.OpenElement(10, "span");
        builder.AddAttribute(11, "class", "dx-switch-track");
        builder.AddAttribute(12, "aria-hidden", "true");
        builder.OpenElement(13, "span");
        builder.AddAttribute(14, "class", "dx-switch-thumb");
        builder.CloseElement();
        builder.CloseElement();

        if (!string.IsNullOrEmpty(Label))
        {
            builder.OpenElement(15, "span");
            builder.AddAttribute(16, "class", "dx-switch-label");
            builder.AddContent(17, Label);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private Task OnChangeAsync(ChangeEventArgs args)
    {
        bool isOn = args.Value is bool value && value;
        return ValueChanged.HasDelegate ? ValueChanged.InvokeAsync(isOn) : Task.CompletedTask;
    }
}
