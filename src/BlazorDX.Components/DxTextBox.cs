using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A labelled single-line text input on a native <c>&lt;input&gt;</c> for free
/// accessibility, with CSS-variable styling. Two-way bind via <c>@bind-Value</c>.
/// The <see cref="Type"/> selects the HTML input type (text, email, url, tel,
/// search). For passwords use <see cref="DxPassword"/>; for numbers,
/// <see cref="DxNumeric{TValue}"/>. Styling is token-driven (see dx-input.css).
/// </summary>
public sealed class DxTextBox : ComponentBase
{
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? Placeholder { get; set; }

    /// <summary>HTML input type: text (default), email, url, tel, or search.</summary>
    [Parameter] public string Type { get; set; } = "text";

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Bind on each keystroke (<c>oninput</c>) instead of on commit (<c>onchange</c>).</summary>
    [Parameter] public bool Immediate { get; set; }

    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "label");
        builder.AddAttribute(1, "class", $"dx-field {Class}".TrimEnd());

        if (!string.IsNullOrEmpty(Label))
        {
            builder.OpenElement(2, "span");
            builder.AddAttribute(3, "class", "dx-field-label");
            builder.AddContent(4, Label);
            builder.CloseElement();
        }

        builder.OpenElement(5, "input");
        builder.AddAttribute(6, "class", "dx-input");
        builder.AddAttribute(7, "type", Type);
        builder.AddAttribute(8, "value", Value);
        builder.AddAttribute(9, "disabled", Disabled);
        builder.AddAttribute(10, "readonly", ReadOnly);
        if (!string.IsNullOrEmpty(Placeholder))
        {
            builder.AddAttribute(11, "placeholder", Placeholder);
        }

        if (!string.IsNullOrEmpty(AriaLabel))
        {
            builder.AddAttribute(12, "aria-label", AriaLabel);
        }

        builder.AddAttribute(13, Immediate ? "oninput" : "onchange",
            EventCallback.Factory.Create<ChangeEventArgs>(this, OnChangeAsync));
        builder.CloseElement();

        builder.CloseElement();
    }

    private Task OnChangeAsync(ChangeEventArgs args)
    {
        string? text = args.Value as string;
        return ValueChanged.HasDelegate ? ValueChanged.InvokeAsync(text) : Task.CompletedTask;
    }
}
