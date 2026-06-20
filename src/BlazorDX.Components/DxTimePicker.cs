using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A time input backed by the native <c>&lt;input type="time"&gt;</c> (so the picker,
/// keyboard, and locale formatting come from the platform). A leaf component,
/// consistent with the other native-backed inputs; styling reuses dx-input.css.
/// </summary>
public sealed class DxTimePicker : ComponentBase
{
    /// <summary>The selected time (two-way bindable via <c>@bind-Value</c>).</summary>
    [Parameter] public TimeOnly? Value { get; set; }

    /// <summary>Raised when the time changes.</summary>
    [Parameter] public EventCallback<TimeOnly?> ValueChanged { get; set; }

    /// <summary>Earliest allowed time.</summary>
    [Parameter] public TimeOnly? Min { get; set; }

    /// <summary>Latest allowed time.</summary>
    [Parameter] public TimeOnly? Max { get; set; }

    /// <summary>Step in seconds; values under 60 enable a seconds field.</summary>
    [Parameter] public int StepSeconds { get; set; } = 60;

    /// <summary>Disables the input.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Optional field label.</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>Accessible label (falls back to <see cref="Label"/>).</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Extra CSS classes appended to the field.</summary>
    [Parameter] public string? Class { get; set; }

    private string Format => StepSeconds % 60 == 0 ? "HH\\:mm" : "HH\\:mm\\:ss";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "label");
        builder.AddAttribute(1, "class", $"dx-field {Class}".TrimEnd());

        if (Label is not null)
        {
            builder.OpenElement(2, "span");
            builder.AddAttribute(3, "class", "dx-field-label");
            builder.AddContent(4, Label);
            builder.CloseElement();
        }

        builder.OpenElement(5, "input");
        builder.AddAttribute(6, "class", "dx-input");
        builder.AddAttribute(7, "type", "time");
        builder.AddAttribute(8, "value", Value?.ToString(Format, CultureInfo.InvariantCulture) ?? string.Empty);
        builder.AddAttribute(9, "step", StepSeconds.ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(10, "disabled", Disabled);
        builder.AddAttribute(11, "aria-label", AriaLabel ?? Label);
        if (Min is TimeOnly min)
        {
            builder.AddAttribute(12, "min", min.ToString(Format, CultureInfo.InvariantCulture));
        }

        if (Max is TimeOnly max)
        {
            builder.AddAttribute(13, "max", max.ToString(Format, CultureInfo.InvariantCulture));
        }

        builder.AddAttribute(14, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, OnInput));
        builder.CloseElement();

        builder.CloseElement();
    }

    private async Task OnInput(ChangeEventArgs args)
    {
        if (Disabled)
        {
            return;
        }

        string text = args.Value?.ToString() ?? string.Empty;
        TimeOnly? parsed = TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out TimeOnly value) ? value : null;
        if (parsed != Value && ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(parsed);
        }
    }
}
