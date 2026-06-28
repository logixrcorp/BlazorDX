using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Primitives.Inputs;

/// <summary>How the calendar resolves clicks into a selection.</summary>
public enum CalendarSelectionMode
{
    /// <summary>One date at a time (<c>Value</c>).</summary>
    Single,

    /// <summary>A start/end date range (<c>RangeStart</c>/<c>RangeEnd</c>).</summary>
    Range,
}

/// <summary>An inclusive selected date range (either end may be null while a range is being picked).</summary>
public readonly record struct CalendarRange(DateOnly? Start, DateOnly? End);

/// <summary>
/// Tier 1 headless <em>inline</em> calendar: an always-visible month grid with month/year navigation,
/// single or range date selection, min/max bounds, an arbitrary disabled-date predicate, and a set of
/// "marked" dates the styled layer can decorate. The focused day is surfaced via the WAI-ARIA grid
/// pattern (aria-activedescendant) with full 2-D keyboard navigation. The week starts on the culture's
/// first day of week. Renders no markup itself — the styled layer (<c>DxCalendar</c>) turns this into UI.
/// </summary>
public class CalendarPrimitive : ComponentBase
{
    /// <summary>Always six weeks so the grid never reflows as months change height.</summary>
    private const int CellCount = 42;

    [Parameter] public CalendarSelectionMode SelectionMode { get; set; } = CalendarSelectionMode.Single;

    // ---- Single selection ----
    [Parameter] public DateOnly? Value { get; set; }

    [Parameter] public EventCallback<DateOnly?> ValueChanged { get; set; }

    // ---- Range selection ----
    [Parameter] public DateOnly? RangeStart { get; set; }

    [Parameter] public EventCallback<DateOnly?> RangeStartChanged { get; set; }

    [Parameter] public DateOnly? RangeEnd { get; set; }

    [Parameter] public EventCallback<DateOnly?> RangeEndChanged { get; set; }

    /// <summary>Raised once a range is completed (both ends chosen), ordered start ≤ end.</summary>
    [Parameter] public EventCallback<CalendarRange> OnRangeSelected { get; set; }

    /// <summary>Raised when a date is chosen in <see cref="CalendarSelectionMode.Single"/> mode.</summary>
    [Parameter] public EventCallback<DateOnly> OnDateSelected { get; set; }

    /// <summary>The first day of the month shown. Two-way bindable; defaults to the current month.</summary>
    [Parameter] public DateOnly? Month { get; set; }

    [Parameter] public EventCallback<DateOnly> MonthChanged { get; set; }

    [Parameter] public DateOnly? Min { get; set; }

    [Parameter] public DateOnly? Max { get; set; }

    /// <summary>Optional predicate marking individual dates unselectable (in addition to Min/Max).</summary>
    [Parameter] public Func<DateOnly, bool>? IsDateDisabled { get; set; }

    /// <summary>Dates the styled layer decorates (e.g. an event dot). Selection-independent.</summary>
    [Parameter] public IReadOnlyCollection<DateOnly>? MarkedDates { get; set; }

    /// <summary>Culture for month/weekday text and the first day of week (defaults to current UI culture).</summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    private DateOnly viewMonth;
    private DateOnly focusedDate;
    private bool initialized;

    // While a range is half-picked, the day under the pointer previews the in-progress span.
    private DateOnly? hoverDate;

    // Materialized once per parameter set so per-cell IsMarked is O(1) over 42 cells.
    private HashSet<DateOnly>? markedSet;

    private CultureInfo Fmt => Culture ?? CultureInfo.CurrentCulture;

    protected DateOnly ViewMonth => viewMonth;

    protected DateOnly FocusedDate => focusedDate;

    protected string MonthLabel => viewMonth.ToString("MMMM yyyy", Fmt);

    protected override void OnParametersSet()
    {
        if (Month is DateOnly controlled)
        {
            viewMonth = FirstOfMonth(controlled);
        }
        else if (!initialized)
        {
            viewMonth = FirstOfMonth(Value ?? RangeStart ?? Today());
        }

        if (!initialized)
        {
            // Land the keyboard cursor on a day that is actually in the visible grid: the value, else
            // today when this is today's month, else the 1st of the shown month.
            DateOnly fallback = IsInViewMonth(Today()) ? Today() : viewMonth;
            focusedDate = ClampFocus(Value ?? RangeStart ?? fallback);
            initialized = true;
        }

        markedSet = MarkedDates is { Count: > 0 } ? new HashSet<DateOnly>(MarkedDates) : null;
    }

    /// <summary>Abbreviated weekday headers, ordered from the culture's first day of week.</summary>
    protected IReadOnlyList<string> WeekdayHeaders()
    {
        string[] names = Fmt.DateTimeFormat.AbbreviatedDayNames;   // index 0 == Sunday
        int first = (int)Fmt.DateTimeFormat.FirstDayOfWeek;
        string[] ordered = new string[7];
        for (int i = 0; i < 7; i++)
        {
            ordered[i] = names[(first + i) % 7];
        }

        return ordered;
    }

    /// <summary>The 42 grid days, starting on the first-day-of-week on/before the 1st of the month.</summary>
    protected IEnumerable<DateOnly> CalendarDays()
    {
        DateOnly start = GridStart();
        for (int i = 0; i < CellCount; i++)
        {
            yield return start.AddDays(i);
        }
    }

    private DateOnly GridStart()
    {
        int first = (int)Fmt.DateTimeFormat.FirstDayOfWeek;
        int offset = ((int)viewMonth.DayOfWeek - first + 7) % 7;
        return viewMonth.AddDays(-offset);
    }

    protected string DayId(DateOnly date) => $"{GridId}-d-{date:yyyyMMdd}";

    /// <summary>Stable per-instance id base for the grid and its cells (aria-activedescendant target).</summary>
    protected string GridId { get; } = $"dx-cal-{Guid.NewGuid():N}";

    protected bool IsToday(DateOnly date) => date == Today();

    protected bool IsOutsideMonth(DateOnly date) => date.Month != viewMonth.Month || date.Year != viewMonth.Year;

    private bool IsInViewMonth(DateOnly date) => !IsOutsideMonth(date);

    protected bool IsFocused(DateOnly date) => date == focusedDate;

    protected bool IsMarked(DateOnly date) => markedSet?.Contains(date) ?? false;

    protected bool IsDisabled(DateOnly date) =>
        (Min is DateOnly min && date < min)
        || (Max is DateOnly max && date > max)
        || (IsDateDisabled?.Invoke(date) ?? false);

    protected bool IsSelected(DateOnly date) => SelectionMode == CalendarSelectionMode.Single
        ? Value == date
        : date == RangeStart || date == RangeEnd;

    protected bool IsRangeStart(DateOnly date) => SelectionMode == CalendarSelectionMode.Range && date == RangeStart;

    protected bool IsRangeEnd(DateOnly date) => SelectionMode == CalendarSelectionMode.Range && date == RangeEnd;

    /// <summary>Whether a date falls inside the selected range, or the in-progress hover preview.</summary>
    protected bool IsInRange(DateOnly date)
    {
        if (SelectionMode != CalendarSelectionMode.Range || RangeStart is not DateOnly start)
        {
            return false;
        }

        DateOnly otherEnd = RangeEnd ?? hoverDate ?? start;
        DateOnly lo = start <= otherEnd ? start : otherEnd;
        DateOnly hi = start <= otherEnd ? otherEnd : start;
        return date >= lo && date <= hi;
    }

    // ---- Navigation ----

    protected Task PreviousMonthAsync() => MoveViewAsync(-1);

    protected Task NextMonthAsync() => MoveViewAsync(1);

    protected Task GoToTodayAsync()
    {
        focusedDate = ClampFocus(Today());
        return SetViewAsync(FirstOfMonth(Today()));
    }

    private Task MoveViewAsync(int months)
    {
        focusedDate = ClampFocus(focusedDate.AddMonths(months));
        return SetViewAsync(viewMonth.AddMonths(months));
    }

    private Task SetViewAsync(DateOnly month)
    {
        viewMonth = FirstOfMonth(month);
        return MonthChanged.HasDelegate ? MonthChanged.InvokeAsync(viewMonth) : Task.CompletedTask;
    }

    // ---- Selection ----

    /// <summary>Selects <paramref name="date"/> (single mode) or advances the range (range mode).</summary>
    protected async Task SelectAsync(DateOnly date)
    {
        if (IsDisabled(date))
        {
            return;
        }

        if (SelectionMode == CalendarSelectionMode.Single)
        {
            if (ValueChanged.HasDelegate)
            {
                await ValueChanged.InvokeAsync(date);
            }

            if (OnDateSelected.HasDelegate)
            {
                await OnDateSelected.InvokeAsync(date);
            }

            return;
        }

        // Range: first click (or a click after a complete range) starts a fresh range; the
        // second click closes it, ordered so start ≤ end.
        bool rangeComplete = RangeStart is not null && RangeEnd is not null;
        if (RangeStart is null || rangeComplete)
        {
            hoverDate = null;
            await SetRangeAsync(date, null);
            return;
        }

        DateOnly start = RangeStart.Value;
        DateOnly lo = date <= start ? date : start;
        DateOnly hi = date <= start ? start : date;
        hoverDate = null;
        await SetRangeAsync(lo, hi);
        if (OnRangeSelected.HasDelegate)
        {
            await OnRangeSelected.InvokeAsync(new CalendarRange(lo, hi));
        }
    }

    private async Task SetRangeAsync(DateOnly? start, DateOnly? end)
    {
        if (RangeStartChanged.HasDelegate)
        {
            await RangeStartChanged.InvokeAsync(start);
        }

        if (RangeEndChanged.HasDelegate)
        {
            await RangeEndChanged.InvokeAsync(end);
        }
    }

    /// <summary>Sets the hovered day for the range preview (range mode, only while half-picked).</summary>
    protected void SetHover(DateOnly date)
    {
        if (SelectionMode == CalendarSelectionMode.Range && RangeStart is not null && RangeEnd is null)
        {
            hoverDate = date;
            StateHasChanged();
        }
    }

    protected void ClearHover()
    {
        if (hoverDate is not null)
        {
            hoverDate = null;
            StateHasChanged();
        }
    }

    // ---- Keyboard (WAI-ARIA grid pattern) ----

    protected async Task OnGridKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowLeft": MoveFocus(-1); break;
            case "ArrowRight": MoveFocus(1); break;
            case "ArrowUp": MoveFocus(-7); break;
            case "ArrowDown": MoveFocus(7); break;
            case "Home": MoveFocus(-WeekdayOffset(focusedDate)); break;
            case "End": MoveFocus(6 - WeekdayOffset(focusedDate)); break;
            case "PageUp": MoveFocusMonths(args.ShiftKey ? -12 : -1); break;
            case "PageDown": MoveFocusMonths(args.ShiftKey ? 12 : 1); break;
            case "Enter" or " ": await SelectAsync(focusedDate); break;
            default: return;
        }
    }

    private void MoveFocus(int days)
    {
        focusedDate = focusedDate.AddDays(days);
        viewMonth = FirstOfMonth(focusedDate);
        _ = SyncViewMonth();
    }

    private void MoveFocusMonths(int months)
    {
        focusedDate = focusedDate.AddMonths(months);
        viewMonth = FirstOfMonth(focusedDate);
        _ = SyncViewMonth();
    }

    private Task SyncViewMonth() =>
        MonthChanged.HasDelegate ? MonthChanged.InvokeAsync(viewMonth) : Task.CompletedTask;

    // Days from the week's first day to this date (0..6), honoring the culture's first day of week.
    private int WeekdayOffset(DateOnly date) =>
        ((int)date.DayOfWeek - (int)Fmt.DateTimeFormat.FirstDayOfWeek + 7) % 7;

    private DateOnly ClampFocus(DateOnly date)
    {
        if (Min is DateOnly min && date < min)
        {
            return min;
        }

        return Max is DateOnly max && date > max ? max : date;
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
}
