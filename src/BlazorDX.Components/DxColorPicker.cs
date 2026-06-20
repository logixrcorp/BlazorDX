using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A color picker backed by a native <c>&lt;input type="color"&gt;</c> (so the OS
/// picker, keyboard, and ARIA come for free), with an optional hex readout and
/// preset swatches. A leaf component, consistent with the other native-backed
/// inputs. Styling is CSS-variable driven (see dx-input.css).
/// </summary>
public sealed class DxColorPicker : ComponentBase
{
    /// <summary>The selected color as a hex string (e.g. <c>#2563eb</c>).</summary>
    [Parameter] public string Value { get; set; } = "#000000";

    /// <summary>Raised when the color changes.</summary>
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Optional preset swatches shown beside the picker.</summary>
    [Parameter] public IReadOnlyList<string>? Presets { get; set; }

    /// <summary>Show the hex value as text.</summary>
    [Parameter] public bool ShowValue { get; set; } = true;

    /// <summary>Disables interaction.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Accessible label.</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Extra CSS classes appended to the wrapper.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", $"dx-colorpicker {Class}".TrimEnd());

        builder.OpenElement(2, "input");
        builder.AddAttribute(3, "type", "color");
        builder.AddAttribute(4, "class", "dx-colorpicker-input");
        builder.AddAttribute(5, "value", Value);
        builder.AddAttribute(6, "disabled", Disabled);
        if (AriaLabel is not null)
        {
            builder.AddAttribute(7, "aria-label", AriaLabel);
        }

        builder.AddAttribute(8, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, OnInput));
        builder.CloseElement();

        if (ShowValue)
        {
            builder.OpenElement(9, "span");
            builder.AddAttribute(10, "class", "dx-colorpicker-value");
            builder.AddContent(11, Value);
            builder.CloseElement();
        }

        if (Presets is { Count: > 0 })
        {
            builder.OpenElement(12, "span");
            builder.AddAttribute(13, "class", "dx-colorpicker-presets");
            foreach (string preset in Presets)
            {
                string captured = preset;
                builder.OpenElement(14, "button");
                builder.SetKey(preset);
                builder.AddAttribute(15, "type", "button");
                builder.AddAttribute(16, "class", "dx-colorpicker-swatch");
                builder.AddAttribute(17, "style", $"background:{preset}");
                builder.AddAttribute(18, "aria-label", $"Use {preset}");
                builder.AddAttribute(19, "disabled", Disabled);
                builder.AddAttribute(20, "onclick", EventCallback.Factory.Create(this, () => SetAsync(captured)));
                builder.CloseElement();
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private Task OnInput(ChangeEventArgs args) => SetAsync(args.Value?.ToString() ?? Value);

    private async Task SetAsync(string color)
    {
        if (!Disabled && color != Value && ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(color);
        }
    }
}
