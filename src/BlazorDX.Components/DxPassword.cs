using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A labelled password field with a show/hide toggle. Built on a native
/// <c>&lt;input&gt;</c> whose type flips between <c>password</c> and <c>text</c>.
/// Two-way bind via <c>@bind-Value</c>. Styling is token-driven (see dx-input.css).
/// </summary>
public sealed class DxPassword : ComponentBase
{
    private bool revealed;

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? Placeholder { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Show the show/hide reveal toggle (default true).</summary>
    [Parameter] public bool AllowReveal { get; set; } = true;

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

        builder.OpenElement(5, "span");
        builder.AddAttribute(6, "class", "dx-input-wrap");

        builder.OpenElement(7, "input");
        builder.AddAttribute(8, "class", "dx-input dx-input-affixed");
        builder.AddAttribute(9, "type", revealed ? "text" : "password");
        builder.AddAttribute(10, "value", Value);
        builder.AddAttribute(11, "disabled", Disabled);
        builder.AddAttribute(12, "autocomplete", "current-password");
        if (!string.IsNullOrEmpty(Placeholder))
        {
            builder.AddAttribute(13, "placeholder", Placeholder);
        }

        if (!string.IsNullOrEmpty(AriaLabel))
        {
            builder.AddAttribute(14, "aria-label", AriaLabel);
        }

        builder.AddAttribute(15, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, OnChangeAsync));
        builder.CloseElement();

        if (AllowReveal)
        {
            builder.OpenElement(16, "button");
            builder.AddAttribute(17, "type", "button");
            builder.AddAttribute(18, "class", "dx-input-affix");
            builder.AddAttribute(19, "aria-pressed", revealed ? "true" : "false");
            builder.AddAttribute(20, "aria-label", revealed ? "Hide password" : "Show password");
            builder.AddAttribute(21, "disabled", Disabled);
            builder.AddAttribute(22, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, ToggleReveal));
            builder.AddContent(23, revealed ? "🙈" : "👁");
            builder.CloseElement();
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private void ToggleReveal() => revealed = !revealed;

    private Task OnChangeAsync(ChangeEventArgs args)
    {
        string? text = args.Value as string;
        return ValueChanged.HasDelegate ? ValueChanged.InvokeAsync(text) : Task.CompletedTask;
    }
}
