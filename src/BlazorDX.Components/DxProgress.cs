using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A progress bar. Determinate when <see cref="Value"/> is set (0–100); indeterminate
/// (animated) when null. WAI-ARIA progressbar. Styling via dx-layout.css.
/// </summary>
public sealed class DxProgress : ComponentBase
{
    [Parameter] public double? Value { get; set; }

    [Parameter] public string? Class { get; set; }

    private bool Indeterminate => Value is null;

    private double Clamped => Math.Clamp(Value ?? 0, 0, 100);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-progress {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "progressbar");
        builder.AddAttribute(3, "aria-valuemin", 0);
        builder.AddAttribute(4, "aria-valuemax", 100);
        if (!Indeterminate)
        {
            builder.AddAttribute(5, "aria-valuenow", Clamped);
        }

        builder.OpenElement(6, "div");
        builder.AddAttribute(7, "class", Indeterminate ? "dx-progress-fill dx-progress-indeterminate" : "dx-progress-fill");
        if (!Indeterminate)
        {
            builder.AddAttribute(8, "style", $"width:{Clamped.ToString("0.#", CultureInfo.InvariantCulture)}%;");
        }

        builder.CloseElement();
        builder.CloseElement();
    }
}
