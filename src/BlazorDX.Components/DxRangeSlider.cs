using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A two-thumb range slider built from two overlaid native range inputs (so keyboard
/// and ARIA come for free) plus a fill bar between the thumbs. The low/high values
/// are clamped so they never cross. A leaf component; styling is CSS-variable driven
/// (see dx-display.css).
/// </summary>
public sealed class DxRangeSlider : ComponentBase
{
    /// <summary>The lower bound (two-way bindable via <c>@bind-Low</c>).</summary>
    [Parameter] public double Low { get; set; }

    /// <summary>Raised when the lower bound changes.</summary>
    [Parameter] public EventCallback<double> LowChanged { get; set; }

    /// <summary>The upper bound (two-way bindable via <c>@bind-High</c>).</summary>
    [Parameter] public double High { get; set; } = 100;

    /// <summary>Raised when the upper bound changes.</summary>
    [Parameter] public EventCallback<double> HighChanged { get; set; }

    /// <summary>Minimum value.</summary>
    [Parameter] public double Min { get; set; }

    /// <summary>Maximum value.</summary>
    [Parameter] public double Max { get; set; } = 100;

    /// <summary>Step increment.</summary>
    [Parameter] public double Step { get; set; } = 1;

    /// <summary>Disables interaction.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Show the current range beside the slider.</summary>
    [Parameter] public bool ShowValue { get; set; }

    /// <summary>Accessible label prefix for the two thumbs.</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Extra CSS classes appended to the wrapper.</summary>
    [Parameter] public string? Class { get; set; }

    private double Span => Max - Min <= 0 ? 1 : Max - Min;

    private double LowPercent => (Low - Min) / Span * 100;

    private double HighPercent => (High - Min) / Span * 100;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", $"dx-range {Class}".TrimEnd());

        // The overlay box holds the track, fill, and both range inputs.
        builder.OpenElement(2, "span");
        builder.AddAttribute(3, "class", "dx-range-area");

        builder.OpenElement(4, "span");
        builder.AddAttribute(5, "class", "dx-range-track");
        builder.CloseElement();

        builder.OpenElement(6, "span");
        builder.AddAttribute(7, "class", "dx-range-fill");
        builder.AddAttribute(8, "style", string.Create(CultureInfo.InvariantCulture,
            $"left:{LowPercent:0.##}%;width:{HighPercent - LowPercent:0.##}%"));
        builder.CloseElement();

        BuildThumb(builder, 10, "dx-range-low", Low, isLow: true);
        BuildThumb(builder, 30, "dx-range-high", High, isLow: false);

        builder.CloseElement();   // .dx-range-area

        if (ShowValue)
        {
            builder.OpenElement(50, "span");
            builder.AddAttribute(51, "class", "dx-range-value");
            builder.AddContent(52, string.Create(CultureInfo.InvariantCulture, $"{Low:0.##} – {High:0.##}"));
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildThumb(RenderTreeBuilder builder, int seq, string cssClass, double value, bool isLow)
    {
        builder.OpenElement(seq, "input");
        builder.AddAttribute(seq + 1, "type", "range");
        builder.AddAttribute(seq + 2, "class", cssClass);
        builder.AddAttribute(seq + 3, "min", Inv(Min));
        builder.AddAttribute(seq + 4, "max", Inv(Max));
        builder.AddAttribute(seq + 5, "step", Inv(Step));
        builder.AddAttribute(seq + 6, "value", Inv(value));
        builder.AddAttribute(seq + 7, "disabled", Disabled);
        builder.AddAttribute(seq + 8, "aria-label", $"{AriaLabel} {(isLow ? "minimum" : "maximum")}".TrimStart());
        builder.AddAttribute(seq + 9, "oninput",
            EventCallback.Factory.Create<ChangeEventArgs>(this, args => OnInput(args, isLow)));
        builder.CloseElement();
    }

    private async Task OnInput(ChangeEventArgs args, bool isLow)
    {
        if (Disabled || !double.TryParse(args.Value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
        {
            return;
        }

        if (isLow)
        {
            double clamped = Math.Min(parsed, High);   // never cross above the upper thumb
            if (clamped != Low && LowChanged.HasDelegate)
            {
                await LowChanged.InvokeAsync(clamped);
            }
        }
        else
        {
            double clamped = Math.Max(parsed, Low);     // never cross below the lower thumb
            if (clamped != High && HighChanged.HasDelegate)
            {
                await HighChanged.InvokeAsync(clamped);
            }
        }
    }

    private static string Inv(double value) => value.ToString(CultureInfo.InvariantCulture);
}
