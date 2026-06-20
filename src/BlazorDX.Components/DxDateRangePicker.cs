using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A start/end date range, composed from two <see cref="DxDatePicker"/>s — no new
/// behavior, just the existing calendar popover reused twice. The two ends are kept
/// consistent: picking a start after the end pushes the end out, and an end before
/// the start is clamped. Styling is CSS-variable driven (see dx-display.css).
/// </summary>
public sealed class DxDateRangePicker : ComponentBase
{
    /// <summary>The range start (two-way bindable via <c>@bind-Start</c>).</summary>
    [Parameter] public DateOnly? Start { get; set; }

    /// <summary>Raised when the start changes.</summary>
    [Parameter] public EventCallback<DateOnly?> StartChanged { get; set; }

    /// <summary>The range end (two-way bindable via <c>@bind-End</c>).</summary>
    [Parameter] public DateOnly? End { get; set; }

    /// <summary>Raised when the end changes.</summary>
    [Parameter] public EventCallback<DateOnly?> EndChanged { get; set; }

    /// <summary>Culture for the two pickers' formatting.</summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>Extra CSS classes appended to the root.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-daterange {Class}".TrimEnd());

        builder.OpenComponent<DxDatePicker>(2);
        builder.AddComponentParameter(3, "Value", Start);
        builder.AddComponentParameter(4, "ValueChanged", EventCallback.Factory.Create<DateOnly?>(this, OnStartAsync));
        builder.AddComponentParameter(5, "Placeholder", "Start date");
        builder.AddComponentParameter(6, "Culture", Culture);
        builder.CloseComponent();

        builder.OpenElement(7, "span");
        builder.AddAttribute(8, "class", "dx-daterange-sep");
        builder.AddAttribute(9, "aria-hidden", "true");
        builder.AddContent(10, "→");
        builder.CloseElement();

        builder.OpenComponent<DxDatePicker>(11);
        builder.AddComponentParameter(12, "Value", End);
        builder.AddComponentParameter(13, "ValueChanged", EventCallback.Factory.Create<DateOnly?>(this, OnEndAsync));
        builder.AddComponentParameter(14, "Placeholder", "End date");
        builder.AddComponentParameter(15, "Culture", Culture);
        builder.CloseComponent();

        builder.CloseElement();
    }

    private async Task OnStartAsync(DateOnly? start)
    {
        if (StartChanged.HasDelegate)
        {
            await StartChanged.InvokeAsync(start);
        }

        // A start after the current end pushes the end out to keep the range valid.
        if (start is DateOnly s && End is DateOnly e && s > e && EndChanged.HasDelegate)
        {
            await EndChanged.InvokeAsync(start);
        }
    }

    private async Task OnEndAsync(DateOnly? end)
    {
        // Clamp an end before the start up to the start.
        DateOnly? effective = end is DateOnly e && Start is DateOnly s && e < s ? Start : end;
        if (EndChanged.HasDelegate)
        {
            await EndChanged.InvokeAsync(effective);
        }
    }
}
