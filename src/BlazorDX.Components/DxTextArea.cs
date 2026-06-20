using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A labelled multi-line text input on a native <c>&lt;textarea&gt;</c>. Two-way
/// bind via <c>@bind-Value</c>; <see cref="Rows"/> sets the initial height.
/// Styling is token-driven (see dx-input.css).
/// </summary>
public sealed class DxTextArea : ComponentBase
{
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? Placeholder { get; set; }

    [Parameter] public int Rows { get; set; } = 4;

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

        builder.OpenElement(5, "textarea");
        builder.AddAttribute(6, "class", "dx-input dx-textarea");
        builder.AddAttribute(7, "rows", Rows);
        builder.AddAttribute(8, "disabled", Disabled);
        builder.AddAttribute(9, "readonly", ReadOnly);
        if (!string.IsNullOrEmpty(Placeholder))
        {
            builder.AddAttribute(10, "placeholder", Placeholder);
        }

        if (!string.IsNullOrEmpty(AriaLabel))
        {
            builder.AddAttribute(11, "aria-label", AriaLabel);
        }

        builder.AddAttribute(12, Immediate ? "oninput" : "onchange",
            EventCallback.Factory.Create<ChangeEventArgs>(this, OnChangeAsync));
        builder.AddContent(13, Value);
        builder.CloseElement();

        builder.CloseElement();
    }

    private Task OnChangeAsync(ChangeEventArgs args)
    {
        string? text = args.Value as string;
        return ValueChanged.HasDelegate ? ValueChanged.InvokeAsync(text) : Task.CompletedTask;
    }
}
