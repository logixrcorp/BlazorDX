using System.Globalization;
using System.Numerics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A labelled numeric input with stepper buttons, generic over any numeric type
/// via .NET generic math (<see cref="INumber{TSelf}"/>) — so <c>int</c>,
/// <c>decimal</c>, <c>double</c>, etc. all parse and clamp type-safely with no
/// reflection. Two-way bind via <c>@bind-Value</c> (nullable: empty = no value).
/// Styling is token-driven (see dx-input.css).
/// </summary>
/// <typeparam name="TValue">A numeric value type (e.g. <c>int</c>, <c>decimal</c>).</typeparam>
public sealed class DxNumeric<TValue> : ComponentBase
    where TValue : struct, INumber<TValue>
{
    [Parameter] public TValue? Value { get; set; }

    [Parameter] public EventCallback<TValue?> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? Placeholder { get; set; }

    /// <summary>Amount added/removed by the stepper buttons (defaults to one).</summary>
    [Parameter] public TValue Step { get; set; } = TValue.One;

    [Parameter] public TValue? Min { get; set; }

    [Parameter] public TValue? Max { get; set; }

    /// <summary>Standard/custom numeric format string for display (e.g. "F2", "C").</summary>
    [Parameter] public string? Format { get; set; }

    /// <summary>Culture for display and parsing (defaults to the current UI culture).</summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public string? Class { get; set; }

    private bool AtMin => Value is { } v && Min is { } min && v <= min;

    private bool AtMax => Value is { } v && Max is { } max && v >= max;

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
        builder.AddAttribute(6, "class", "dx-num");
        builder.AddAttribute(7, "role", "group");

        BuildStepButton(builder, 10, "−", decrement: true, disabled: AtMin);

        builder.OpenElement(20, "input");
        builder.AddAttribute(21, "class", "dx-input dx-num-input");
        builder.AddAttribute(22, "inputmode", "decimal");
        builder.AddAttribute(23, "value", Display());
        builder.AddAttribute(24, "disabled", Disabled);
        builder.AddAttribute(25, "role", "spinbutton");
        if (Min is { } mn)
        {
            builder.AddAttribute(26, "aria-valuemin", mn.ToString(null, CultureInfo.InvariantCulture));
        }

        if (Max is { } mx)
        {
            builder.AddAttribute(27, "aria-valuemax", mx.ToString(null, CultureInfo.InvariantCulture));
        }

        if (Value is { } cur)
        {
            builder.AddAttribute(28, "aria-valuenow", cur.ToString(null, CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrEmpty(Placeholder))
        {
            builder.AddAttribute(29, "placeholder", Placeholder);
        }

        if (!string.IsNullOrEmpty(AriaLabel))
        {
            builder.AddAttribute(30, "aria-label", AriaLabel);
        }

        builder.AddAttribute(31, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, OnChangeAsync));
        builder.AddAttribute(32, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));
        builder.CloseElement();

        BuildStepButton(builder, 40, "+", decrement: false, disabled: AtMax);

        builder.CloseElement();
        builder.CloseElement();
    }

    private void BuildStepButton(RenderTreeBuilder builder, int seq, string glyph, bool decrement, bool disabled)
    {
        builder.OpenElement(seq, "button");
        builder.AddAttribute(seq + 1, "type", "button");
        builder.AddAttribute(seq + 2, "class", "dx-num-step");
        builder.AddAttribute(seq + 3, "tabindex", "-1");
        builder.AddAttribute(seq + 4, "aria-hidden", "true");
        builder.AddAttribute(seq + 5, "disabled", Disabled || disabled);
        builder.AddAttribute(seq + 6, "onclick",
            EventCallback.Factory.Create<MouseEventArgs>(this, () => StepAsync(decrement)));
        builder.AddContent(seq + 7, glyph);
        builder.CloseElement();
    }

    // Display/parse use the bound culture (e.g. comma decimals in de-DE); the
    // aria-value* attributes stay invariant because ARIA requires plain numbers.
    private CultureInfo Fmt => Culture ?? CultureInfo.CurrentCulture;

    private string Display() => Value is { } v ? v.ToString(Format, Fmt) : string.Empty;

    private Task OnKeyDownAsync(KeyboardEventArgs args) => args.Key switch
    {
        "ArrowUp" => StepAsync(decrement: false),
        "ArrowDown" => StepAsync(decrement: true),
        _ => Task.CompletedTask,
    };

    private Task StepAsync(bool decrement)
    {
        TValue current = Value ?? TValue.Zero;
        TValue next = decrement ? current - Step : current + Step;
        return CommitAsync(Clamp(next));
    }

    private Task OnChangeAsync(ChangeEventArgs args)
    {
        string text = args.Value as string ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return CommitAsync(null);
        }

        return TValue.TryParse(text, NumberStyles.Any, Fmt, out TValue parsed)
            ? CommitAsync(Clamp(parsed))
            : CommitAsync(Value);   // reject: re-render restores the last valid value
    }

    private TValue Clamp(TValue value)
    {
        if (Min is { } min && value < min)
        {
            return min;
        }

        return Max is { } max && value > max ? max : value;
    }

    private Task CommitAsync(TValue? value)
    {
        Value = value;
        return ValueChanged.HasDelegate ? ValueChanged.InvokeAsync(value) : Task.CompletedTask;
    }
}
