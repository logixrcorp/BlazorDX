using System.Globalization;
using BlazorDX.Primitives.Inputs;
using BlazorDX.Primitives.Motion;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled date picker. Inherits all behavior from
/// <see cref="DatePickerPrimitive"/> and renders a trigger plus an anchored
/// calendar with month navigation and a keyboard-navigable day grid. Styling is
/// CSS-variable driven (see dx-layout.css).
/// </summary>
public sealed class DxDatePicker : DatePickerPrimitive
{
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-date-root {Class}".TrimEnd());

        builder.OpenElement(2, "button");
        builder.AddAttribute(3, "id", AnchorId);
        builder.AddAttribute(4, "type", "button");
        builder.AddAttribute(5, "class", "dx-date-trigger");
        builder.AddAttribute(6, "aria-haspopup", "dialog");
        builder.AddAttribute(7, "aria-expanded", IsOpen ? "true" : "false");
        builder.AddAttribute(8, "onclick", EventCallback.Factory.Create(this, Toggle));
        builder.AddAttribute(9, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnTriggerKeyDownAsync));
        builder.AddEventPreventDefaultAttribute(10, "onkeydown", true);

        builder.OpenElement(11, "span");
        builder.AddAttribute(12, "class", HasValue ? "dx-date-value" : "dx-date-value dx-date-placeholder");
        builder.AddContent(13, DisplayText);
        builder.CloseElement();

        builder.OpenElement(14, "span");
        builder.AddAttribute(15, "class", "dx-date-icon");
        builder.AddAttribute(16, "aria-hidden", "true");
        builder.AddContent(17, "\U0001F4C5");
        builder.CloseElement();

        builder.CloseElement();

        builder.OpenComponent<PresenceBoundary>(18);
        builder.AddComponentParameter(19, nameof(PresenceBoundary.Visible), IsOpen);
        builder.AddComponentParameter(20, nameof(PresenceBoundary.ExitDurationMs), ExitDurationMs);
        builder.AddComponentParameter(21, nameof(PresenceBoundary.EnterClass), "dx-popover dx-popover-enter");
        builder.AddComponentParameter(22, nameof(PresenceBoundary.LeaveClass), "dx-popover dx-popover-leave");
        builder.AddComponentParameter(23, nameof(PresenceBoundary.ChildContent), (RenderFragment)RenderCalendar);
        builder.CloseComponent();

        builder.CloseElement();
    }

    private void RenderCalendar(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", PanelId);
        builder.AddAttribute(2, "class", "dx-date-panel");
        builder.AddAttribute(3, "role", "dialog");

        BuildHeader(builder);
        BuildGrid(builder);

        builder.CloseElement();
    }

    private void BuildHeader(RenderTreeBuilder builder)
    {
        builder.OpenElement(4, "div");
        builder.AddAttribute(5, "class", "dx-date-header");

        builder.OpenElement(6, "button");
        builder.AddAttribute(7, "type", "button");
        builder.AddAttribute(8, "class", "dx-date-nav");
        builder.AddAttribute(9, "aria-label", "Previous month");
        builder.AddAttribute(10, "onclick", EventCallback.Factory.Create(this, PreviousMonth));
        builder.AddContent(11, "‹");
        builder.CloseElement();

        builder.OpenElement(12, "span");
        builder.AddAttribute(13, "class", "dx-date-month");
        builder.AddAttribute(14, "aria-live", "polite");
        builder.AddContent(15, MonthLabel);
        builder.CloseElement();

        builder.OpenElement(16, "button");
        builder.AddAttribute(17, "type", "button");
        builder.AddAttribute(18, "class", "dx-date-nav");
        builder.AddAttribute(19, "aria-label", "Next month");
        builder.AddAttribute(20, "onclick", EventCallback.Factory.Create(this, NextMonth));
        builder.AddContent(21, "›");
        builder.CloseElement();

        builder.CloseElement();
    }

    private void BuildGrid(RenderTreeBuilder builder)
    {
        builder.OpenElement(22, "div");
        builder.AddAttribute(23, "class", "dx-date-grid");
        builder.AddAttribute(24, "role", "grid");
        builder.AddAttribute(25, "tabindex", "0");
        builder.AddAttribute(26, "aria-activedescendant", ActiveDescendantId);
        builder.AddAttribute(27, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnGridKeyDownAsync));
        builder.AddEventPreventDefaultAttribute(28, "onkeydown", true);
        builder.AddElementReferenceCapture(29, CaptureGrid);

        builder.OpenElement(30, "div");
        builder.AddAttribute(31, "class", "dx-date-weekdays");
        builder.AddAttribute(32, "aria-hidden", "true");
        foreach (string weekday in WeekdayHeaders)
        {
            builder.OpenElement(33, "span");
            builder.AddContent(34, weekday);
            builder.CloseElement();
        }

        builder.CloseElement();

        builder.OpenElement(35, "div");
        builder.AddAttribute(36, "class", "dx-date-days");
        foreach (DateOnly day in CalendarDays())
        {
            string css = "dx-date-day";
            if (IsOutsideMonth(day))
            {
                css += " dx-date-outside";
            }

            if (IsToday(day))
            {
                css += " dx-date-today";
            }

            if (IsSelected(day))
            {
                css += " dx-date-selected";
            }

            if (IsFocused(day))
            {
                css += " dx-date-focused";
            }

            DateOnly captured = day;
            builder.OpenElement(37, "div");
            builder.SetKey(day);
            builder.AddAttribute(38, "id", DayId(day));
            builder.AddAttribute(39, "role", "gridcell");
            builder.AddAttribute(40, "class", css);
            builder.AddAttribute(41, "aria-selected", IsSelected(day) ? "true" : "false");
            builder.AddAttribute(42, "aria-label", day.ToString("D", CultureInfo.InvariantCulture));
            builder.AddAttribute(43, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
            builder.AddContent(44, day.Day.ToString(CultureInfo.InvariantCulture));
            builder.CloseElement();
        }

        builder.CloseElement();

        builder.CloseElement();
    }
}
