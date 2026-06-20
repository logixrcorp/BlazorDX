using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A numeric slider, backed by a native <c>&lt;input type="range"&gt;</c> so keyboard
/// interaction and the ARIA slider semantics come for free, then styled through CSS
/// variables (see dx-input.css). A leaf component, consistent with the other
/// native-backed inputs (<see cref="DxCheckbox"/>, <see cref="DxSwitch"/>).
/// </summary>
public sealed class DxSlider : ComponentBase
{
    /// <summary>The current value (two-way bindable via <c>@bind-Value</c>).</summary>
    [Parameter] public double Value { get; set; }

    /// <summary>Raised as the value changes.</summary>
    [Parameter] public EventCallback<double> ValueChanged { get; set; }

    /// <summary>Minimum value.</summary>
    [Parameter] public double Min { get; set; }

    /// <summary>Maximum value.</summary>
    [Parameter] public double Max { get; set; } = 100;

    /// <summary>Step increment.</summary>
    [Parameter] public double Step { get; set; } = 1;

    /// <summary>Disables interaction.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Show the current value beside the track.</summary>
    [Parameter] public bool ShowValue { get; set; }

    /// <summary>Accessible label for the slider.</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Extra CSS classes appended to the wrapper.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", $"dx-slider {Class}".TrimEnd());

        builder.OpenElement(2, "input");
        builder.AddAttribute(3, "type", "range");
        builder.AddAttribute(4, "class", "dx-slider-input");
        builder.AddAttribute(5, "min", Inv(Min));
        builder.AddAttribute(6, "max", Inv(Max));
        builder.AddAttribute(7, "step", Inv(Step));
        builder.AddAttribute(8, "value", Inv(Value));
        builder.AddAttribute(9, "disabled", Disabled);
        if (AriaLabel is not null)
        {
            builder.AddAttribute(10, "aria-label", AriaLabel);
        }

        builder.AddAttribute(11, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, OnInput));
        builder.CloseElement();

        if (ShowValue)
        {
            builder.OpenElement(12, "span");
            builder.AddAttribute(13, "class", "dx-slider-value");
            builder.AddContent(14, Inv(Value));
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private async Task OnInput(ChangeEventArgs args)
    {
        if (Disabled)
        {
            return;
        }

        if (double.TryParse(args.Value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed)
            && parsed != Value
            && ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(parsed);
        }
    }

    private static string Inv(double value) => value.ToString(CultureInfo.InvariantCulture);
}
