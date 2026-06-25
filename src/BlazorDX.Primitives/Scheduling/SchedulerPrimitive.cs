using Microsoft.AspNetCore.Components;

namespace BlazorDX.Primitives.Scheduling;

/// <summary>The calendar view rendered by the scheduler.</summary>
public enum SchedulerView
{
    /// <summary>A multi-day time grid (the default).</summary>
    Week,

    /// <summary>A whole-month date grid (weeks as rows, weekdays as columns).</summary>
    Month,

    /// <summary>A single-day time grid.</summary>
    Day,
}

/// <summary>A scheduled event placed on the calendar.</summary>
/// <param name="Title">Event label.</param>
/// <param name="Start">Start timestamp.</param>
/// <param name="End">End timestamp (same day as <paramref name="Start"/> for this view).</param>
/// <param name="Color">Optional CSS color override for the event block.</param>
/// <param name="Category">
/// Optional category/status label. Conveyed as text (never color alone) so the
/// distinction survives for colour-blind and high-contrast users (WCAG 1.4.1).
/// </param>
public readonly record struct SchedulerEvent(
    string Title,
    DateTime Start,
    DateTime End,
    string? Color = null,
    string? Category = null);

/// <summary>One event laid out within a day column.</summary>
/// <param name="Event">The source event.</param>
/// <param name="DayIndex">Zero-based day column.</param>
/// <param name="OffsetHours">Hours from the view's start hour to the block's top.</param>
/// <param name="LengthHours">Block height in hours (clamped to the visible range).</param>
public readonly record struct ScheduledBlock(SchedulerEvent Event, int DayIndex, double OffsetHours, double LengthHours);

/// <summary>
/// Tier 1 headless scheduler. Computes the visible cells for the Week, Month, and
/// Day views and lays each event out (time-positioned blocks for Week/Day, day
/// buckets for Month). Owns view selection, navigation, and a 2-D roving
/// active-cell model (the ARIA "grid" keyboard pattern, mirroring the DataGrid);
/// renders nothing — the styled layer turns the model into markup.
/// </summary>
public class SchedulerPrimitive : ComponentBase
{
    [Parameter] public IReadOnlyList<SchedulerEvent> Events { get; set; } = [];

    /// <summary>First day shown (left column).</summary>
    [Parameter] public DateOnly WeekStart { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Parameter] public EventCallback<DateOnly> WeekStartChanged { get; set; }

    [Parameter] public int DayCount { get; set; } = 7;

    /// <summary>First hour shown (24h).</summary>
    [Parameter] public int StartHour { get; set; } = 8;

    /// <summary>Last hour shown (exclusive upper bound, 24h).</summary>
    [Parameter] public int EndHour { get; set; } = 18;

    /// <summary>The active calendar view.</summary>
    [Parameter] public SchedulerView View { get; set; } = SchedulerView.Week;

    [Parameter] public EventCallback<SchedulerView> ViewChanged { get; set; }

    [Parameter] public EventCallback<SchedulerEvent> OnEventSelected { get; set; }

    /// <summary>The active cell's row (time-grid row or month week), or -1 when none.</summary>
    protected int ActiveRow { get; private set; } = -1;

    /// <summary>The active cell's column (day column or month weekday), or -1 when none.</summary>
    protected int ActiveColumn { get; private set; } = -1;

    /// <summary>Number of day columns visible in the current view.</summary>
    protected int ViewDayCount => View switch
    {
        SchedulerView.Day => 1,
        SchedulerView.Month => 7,
        _ => Math.Max(1, DayCount),
    };

    /// <summary>The day columns, left to right, for the active view.</summary>
    protected IReadOnlyList<DateOnly> Days
    {
        get
        {
            DateOnly first = ViewStart;
            DateOnly[] days = new DateOnly[ViewDayCount];
            for (int i = 0; i < days.Length; i++)
            {
                days[i] = first.AddDays(i);
            }

            return days;
        }
    }

    /// <summary>Hour labels down the time axis (Week/Day views).</summary>
    protected IReadOnlyList<int> Hours
    {
        get
        {
            List<int> hours = new();
            for (int h = StartHour; h < EndHour; h++)
            {
                hours.Add(h);
            }

            return hours;
        }
    }

    protected int HourSpan => Math.Max(1, EndHour - StartHour);

    /// <summary>First day of the active view (Day = WeekStart; Week = WeekStart; Month = first grid cell).</summary>
    protected DateOnly ViewStart => View == SchedulerView.Month ? MonthGridStart : WeekStart;

    /// <summary>First day of the month containing <see cref="WeekStart"/>.</summary>
    protected DateOnly MonthFirst => new(WeekStart.Year, WeekStart.Month, 1);

    /// <summary>The Monday on or before the first of the month (top-left month grid cell).</summary>
    protected DateOnly MonthGridStart
    {
        get
        {
            DateOnly first = MonthFirst;
            int back = ((int)first.DayOfWeek + 6) % 7;   // days since Monday
            return first.AddDays(-back);
        }
    }

    /// <summary>Number of week rows needed to show the whole month (4–6).</summary>
    protected int MonthWeekCount
    {
        get
        {
            int daysInMonth = DateTime.DaysInMonth(WeekStart.Year, WeekStart.Month);
            int lead = ((int)MonthFirst.DayOfWeek + 6) % 7;
            return (int)Math.Ceiling((lead + daysInMonth) / 7.0);
        }
    }

    /// <summary>Whether <paramref name="day"/> falls inside the displayed month.</summary>
    protected bool IsInDisplayedMonth(DateOnly day) => day.Month == WeekStart.Month && day.Year == WeekStart.Year;

    /// <summary>Lays every event that falls within the visible day range and hour range into a block (Week/Day).</summary>
    protected IEnumerable<ScheduledBlock> Blocks()
    {
        DateOnly first = ViewStart;
        int dayCount = ViewDayCount;
        foreach (SchedulerEvent ev in Events)
        {
            int dayIndex = DateOnly.FromDateTime(ev.Start).DayNumber - first.DayNumber;
            if (dayIndex < 0 || dayIndex >= dayCount)
            {
                continue;
            }

            double startH = ev.Start.Hour + (ev.Start.Minute / 60.0);
            double endH = ev.End.Hour + (ev.End.Minute / 60.0);
            if (endH <= startH)
            {
                continue;
            }

            double top = Math.Max(StartHour, startH);
            double bottom = Math.Min(EndHour, endH);
            if (bottom <= top)
            {
                continue;   // entirely outside the visible hours
            }

            yield return new ScheduledBlock(ev, dayIndex, top - StartHour, bottom - top);
        }
    }

    /// <summary>Events whose start date is <paramref name="day"/>, ordered by start time (Month cells).</summary>
    protected IEnumerable<SchedulerEvent> EventsOn(DateOnly day)
    {
        List<SchedulerEvent> matches = new();
        foreach (SchedulerEvent ev in Events)
        {
            if (DateOnly.FromDateTime(ev.Start) == day)
            {
                matches.Add(ev);
            }
        }

        matches.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return matches;
    }

    protected Task SelectAsync(SchedulerEvent ev) =>
        OnEventSelected.HasDelegate ? OnEventSelected.InvokeAsync(ev) : Task.CompletedTask;

    /// <summary>Switches the active view, resetting the active cell to the origin.</summary>
    protected Task SetViewAsync(SchedulerView view)
    {
        if (view == View)
        {
            return Task.CompletedTask;
        }

        View = view;
        ActiveRow = -1;
        ActiveColumn = -1;
        return ViewChanged.HasDelegate ? ViewChanged.InvokeAsync(view) : Task.CompletedTask;
    }

    /// <summary>Moves to the previous period for the active view (week/month/day).</summary>
    protected Task PreviousAsync() => View switch
    {
        SchedulerView.Month => GoToAsync(WeekStart.AddMonths(-1)),
        SchedulerView.Day => GoToAsync(WeekStart.AddDays(-1)),
        _ => GoToAsync(WeekStart.AddDays(-DayCount)),
    };

    /// <summary>Moves to the next period for the active view (week/month/day).</summary>
    protected Task NextAsync() => View switch
    {
        SchedulerView.Month => GoToAsync(WeekStart.AddMonths(1)),
        SchedulerView.Day => GoToAsync(WeekStart.AddDays(1)),
        _ => GoToAsync(WeekStart.AddDays(DayCount)),
    };

    // Retained for source/binary compatibility with the week-only API.
    protected Task PreviousWeekAsync() => GoToAsync(WeekStart.AddDays(-DayCount));

    protected Task NextWeekAsync() => GoToAsync(WeekStart.AddDays(DayCount));

    protected Task TodayAsync() => GoToAsync(DateOnly.FromDateTime(DateTime.Today));

    private Task GoToAsync(DateOnly start)
    {
        WeekStart = start;
        return WeekStartChanged.HasDelegate ? WeekStartChanged.InvokeAsync(start) : Task.CompletedTask;
    }

    // ---- 2-D roving active-cell navigation (ARIA grid pattern) ----

    /// <summary>Rows in the active grid: time-slot rows (Week/Day) or week rows (Month).</summary>
    protected int GridRows => View == SchedulerView.Month ? MonthWeekCount : HourSpan;

    /// <summary>Columns in the active grid: day columns (Week/Day) or weekdays (Month).</summary>
    protected int GridColumns => ViewDayCount;

    protected bool HasActiveCell => ActiveRow >= 0 && ActiveColumn >= 0;

    protected bool IsActiveCell(int row, int column) => row == ActiveRow && column == ActiveColumn;

    /// <summary>The date addressed by the active cell, or null when there is none.</summary>
    protected DateOnly? ActiveDate => CellDate(ActiveRow, ActiveColumn);

    /// <summary>The date addressed by a (row, column) cell in the current view.</summary>
    protected DateOnly? CellDate(int row, int column)
    {
        if (column < 0 || column >= GridColumns)
        {
            return null;
        }

        return View == SchedulerView.Month
            ? MonthGridStart.AddDays((Math.Max(0, row) * 7) + column)
            : WeekStart.AddDays(column);
    }

    /// <summary>The hour addressed by the active cell in a time view, or null in Month view.</summary>
    protected int? CellHour(int row) =>
        View == SchedulerView.Month || row < 0 || row >= HourSpan ? null : StartHour + row;

    /// <summary>Re-anchors the active cell into range after a view or date change.</summary>
    protected void ClampActiveCell()
    {
        if (GridRows <= 0 || GridColumns <= 0)
        {
            ActiveRow = -1;
            ActiveColumn = -1;
            return;
        }

        if (HasActiveCell)
        {
            ActiveRow = Math.Clamp(ActiveRow, 0, GridRows - 1);
            ActiveColumn = Math.Clamp(ActiveColumn, 0, GridColumns - 1);
        }
    }

    /// <summary>Makes (row, column) the active cell (e.g. on click/focus).</summary>
    protected void SetActiveCell(int row, int column)
    {
        if (row >= 0 && row < GridRows && column >= 0 && column < GridColumns)
        {
            ActiveRow = row;
            ActiveColumn = column;
        }
    }

    /// <summary>
    /// Applies an arrow / Home / End key to the active cell. Returns true when the
    /// key was a navigation key (so the caller can re-render and prevent default).
    /// </summary>
    protected bool MoveActiveCell(string key, bool ctrl)
    {
        if (GridRows <= 0 || GridColumns <= 0)
        {
            return false;
        }

        // First navigation key seeds the active cell at the origin.
        if (!HasActiveCell)
        {
            ActiveRow = 0;
            ActiveColumn = 0;
            return true;
        }

        switch (key)
        {
            case "ArrowDown": ActiveRow = Math.Min(GridRows - 1, ActiveRow + 1); break;
            case "ArrowUp": ActiveRow = Math.Max(0, ActiveRow - 1); break;
            case "ArrowRight": ActiveColumn = Math.Min(GridColumns - 1, ActiveColumn + 1); break;
            case "ArrowLeft": ActiveColumn = Math.Max(0, ActiveColumn - 1); break;
            case "Home":
                ActiveColumn = 0;
                if (ctrl)
                {
                    ActiveRow = 0;
                }

                break;
            case "End":
                ActiveColumn = GridColumns - 1;
                if (ctrl)
                {
                    ActiveRow = GridRows - 1;
                }

                break;
            default:
                return false;
        }

        return true;
    }
}
