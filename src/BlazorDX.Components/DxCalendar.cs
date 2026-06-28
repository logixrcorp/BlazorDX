using System.Globalization;
using BlazorDX.Primitives.Inputs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled inline calendar built on <see cref="CalendarPrimitive"/>: an always-visible month
/// grid with previous/next/today navigation, single or range date selection (with a range hover
/// preview), per-day marker dots, and an optional <see cref="DayTemplate"/>. The month is a real
/// ARIA <c>grid</c> (<c>row</c> → <c>gridcell</c>) with 2-D arrow-key navigation, a focus-visible
/// active day surfaced via <c>aria-activedescendant</c>, and a polite live region announcing month
/// changes. Styling is token-driven (see dx-calendar.css).
/// </summary>
public sealed class DxCalendar : CalendarPrimitive
{
    [Parameter] public string? Class { get; set; }

    /// <summary>Optional content rendered inside each day cell (below the day number).</summary>
    [Parameter] public RenderFragment<DateOnly>? DayTemplate { get; set; }

    /// <summary>Accessible label for the grid (default "Calendar").</summary>
    [Parameter] public string Label { get; set; } = "Calendar";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-cal {Class}".TrimEnd());

        BuildHeader(builder);
        BuildLiveRegion(builder);
        BuildGrid(builder);

        builder.CloseElement();
    }

    private void BuildHeader(RenderTreeBuilder builder)
    {
        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-cal-header");

        NavButton(builder, 4, "‹", "Previous month", PreviousMonthAsync);

        builder.OpenElement(10, "span");
        builder.AddAttribute(11, "class", "dx-cal-month");
        builder.AddContent(12, MonthLabel);
        builder.CloseElement();

        NavButton(builder, 14, "›", "Next month", NextMonthAsync);
        NavButton(builder, 20, "Today", "Go to today", GoToTodayAsync);

        builder.CloseElement();
    }

    private void NavButton(RenderTreeBuilder builder, int seq, string text, string label, Func<Task> onClick)
    {
        builder.OpenElement(seq, "button");
        builder.AddAttribute(seq + 1, "type", "button");
        builder.AddAttribute(seq + 2, "class", "dx-cal-nav");
        builder.AddAttribute(seq + 3, "aria-label", label);
        builder.AddAttribute(seq + 4, "onclick", EventCallback.Factory.Create(this, onClick));
        builder.AddContent(seq + 5, text);
        builder.CloseElement();
    }

    // Polite live region announcing the visible month (WCAG 4.1.3).
    private void BuildLiveRegion(RenderTreeBuilder builder)
    {
        builder.OpenElement(28, "div");
        builder.AddAttribute(29, "class", "dx-cal-sr");
        builder.AddAttribute(30, "role", "status");
        builder.AddAttribute(31, "aria-live", "polite");
        builder.AddContent(32, MonthLabel);
        builder.CloseElement();
    }

    private void BuildGrid(RenderTreeBuilder builder)
    {
        builder.OpenElement(40, "div");
        builder.AddAttribute(41, "class", "dx-cal-grid");
        builder.AddAttribute(42, "role", "grid");
        builder.AddAttribute(43, "aria-label", Label);
        builder.AddAttribute(44, "tabindex", "0");
        builder.AddAttribute(45, "aria-activedescendant", DayId(FocusedDate));
        builder.AddAttribute(46, "onkeydown",
            EventCallback.Factory.Create<KeyboardEventArgs>(this, OnGridKeyDownAsync));
        builder.AddEventPreventDefaultAttribute(47, "onkeydown", true);

        // Weekday header row.
        builder.OpenElement(48, "div");
        builder.AddAttribute(49, "class", "dx-cal-weekdays");
        builder.AddAttribute(50, "role", "row");
        foreach (string weekday in WeekdayHeaders())
        {
            builder.OpenElement(51, "span");
            builder.SetKey(weekday);
            builder.AddAttribute(52, "class", "dx-cal-weekday");
            builder.AddAttribute(53, "role", "columnheader");
            builder.AddAttribute(54, "aria-label", weekday);
            builder.AddContent(55, weekday);
            builder.CloseElement();
        }

        builder.CloseElement();

        // Six week rows.
        DateOnly[] days = CalendarDays().ToArray();
        for (int week = 0; week < 6; week++)
        {
            builder.OpenElement(60, "div");
            builder.SetKey(week);
            builder.AddAttribute(61, "class", "dx-cal-week");
            builder.AddAttribute(62, "role", "row");
            for (int col = 0; col < 7; col++)
            {
                BuildDay(builder, days[(week * 7) + col]);
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildDay(RenderTreeBuilder builder, DateOnly day)
    {
        bool disabled = IsDisabled(day);
        bool selected = IsSelected(day);

        builder.OpenElement(70, "div");
        builder.SetKey(day);
        builder.AddAttribute(71, "id", DayId(day));
        builder.AddAttribute(72, "class", DayClass(day, disabled, selected));
        builder.AddAttribute(73, "role", "gridcell");
        builder.AddAttribute(74, "aria-selected", selected ? "true" : "false");
        builder.AddAttribute(75, "aria-label", day.ToString("D", CultureInfo.CurrentCulture));
        if (disabled)
        {
            builder.AddAttribute(76, "aria-disabled", "true");
        }

        if (IsToday(day))
        {
            builder.AddAttribute(77, "aria-current", "date");
        }

        DateOnly captured = day;
        if (!disabled)
        {
            builder.AddAttribute(78, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
            builder.AddAttribute(79, "onmouseover", EventCallback.Factory.Create(this, () => SetHover(captured)));
            builder.AddAttribute(80, "onmouseout", EventCallback.Factory.Create(this, ClearHover));
        }

        builder.OpenElement(81, "span");
        builder.AddAttribute(82, "class", "dx-cal-day-num");
        builder.AddContent(83, day.Day.ToString(CultureInfo.CurrentCulture));
        builder.CloseElement();

        // A per-day template wins; otherwise a marker dot shows for marked dates.
        if (DayTemplate is not null)
        {
            builder.AddContent(84, DayTemplate(day));
        }
        else if (IsMarked(day))
        {
            builder.OpenElement(85, "span");
            builder.AddAttribute(86, "class", "dx-cal-dot");
            builder.AddAttribute(87, "aria-hidden", "true");
            builder.AddContent(88, "●");
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private string DayClass(DateOnly day, bool disabled, bool selected)
    {
        string css = "dx-cal-day";
        if (IsOutsideMonth(day))
        {
            css += " dx-cal-outside";
        }

        if (IsToday(day))
        {
            css += " dx-cal-today";
        }

        if (selected)
        {
            css += " dx-cal-selected";
        }

        if (IsRangeStart(day))
        {
            css += " dx-cal-range-start";
        }

        if (IsRangeEnd(day))
        {
            css += " dx-cal-range-end";
        }

        if (IsInRange(day))
        {
            css += " dx-cal-in-range";
        }

        if (IsFocused(day))
        {
            css += " dx-cal-focused";
        }

        if (IsMarked(day))
        {
            css += " dx-cal-marked";
        }

        if (disabled)
        {
            css += " dx-cal-disabled";
        }

        return css;
    }
}
