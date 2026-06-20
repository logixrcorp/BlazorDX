using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A star rating input. Click or use the keyboard (Arrow keys, Home/End) to set a
/// value; hovering previews. Follows the WAI-ARIA slider pattern. A leaf component:
/// the behavior is small enough that no separate primitive is warranted. Styling is
/// CSS-variable driven (see dx-layout.css).
/// </summary>
public sealed class DxRating : ComponentBase
{
    private int hoverValue;

    [Parameter] public int Value { get; set; }

    [Parameter] public EventCallback<int> ValueChanged { get; set; }

    [Parameter] public int Max { get; set; } = 5;

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public string? Class { get; set; }

    // What to paint: the hover preview if any, otherwise the committed value.
    private int Display => hoverValue > 0 ? hoverValue : Value;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", ReadOnly ? $"dx-rating dx-rating-readonly {Class}".TrimEnd() : $"dx-rating {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "slider");
        builder.AddAttribute(3, "aria-valuemin", 0);
        builder.AddAttribute(4, "aria-valuemax", Max);
        builder.AddAttribute(5, "aria-valuenow", Value);
        builder.AddAttribute(6, "aria-label", $"Rating: {Value} of {Max}");
        builder.AddAttribute(7, "aria-readonly", ReadOnly ? "true" : "false");
        if (!ReadOnly)
        {
            builder.AddAttribute(8, "tabindex", "0");
            builder.AddAttribute(9, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDown));
            builder.AddAttribute(10, "onmouseleave", EventCallback.Factory.Create(this, () => hoverValue = 0));
        }

        for (int i = 1; i <= Max; i++)
        {
            int star = i;
            bool on = star <= Display;

            builder.OpenElement(11, "span");
            builder.SetKey(star);
            builder.AddAttribute(12, "class", on ? "dx-rating-star dx-rating-on" : "dx-rating-star");
            builder.AddAttribute(13, "aria-hidden", "true");
            if (!ReadOnly)
            {
                builder.AddAttribute(14, "onmouseover", EventCallback.Factory.Create(this, () => hoverValue = star));
                builder.AddAttribute(15, "onclick", EventCallback.Factory.Create(this, () => SetValueAsync(star)));
            }

            builder.AddContent(16, on ? "★" : "☆");
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private async Task SetValueAsync(int value)
    {
        if (ReadOnly)
        {
            return;
        }

        int clamped = Math.Clamp(value, 0, Max);
        if (clamped != Value && ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(clamped);
        }
    }

    private async Task OnKeyDown(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowRight" or "ArrowUp": await SetValueAsync(Value + 1); break;
            case "ArrowLeft" or "ArrowDown": await SetValueAsync(Value - 1); break;
            case "Home": await SetValueAsync(0); break;
            case "End": await SetValueAsync(Max); break;
        }
    }
}
