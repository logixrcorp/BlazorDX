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

/// <summary>How often a recurring event repeats.</summary>
public enum RecurrenceFrequency
{
    /// <summary>Every <c>Interval</c> days.</summary>
    Daily,

    /// <summary>Every <c>Interval</c> weeks, on each <c>ByWeekday</c> (or the seed's weekday).</summary>
    Weekly,

    /// <summary>Every <c>Interval</c> months, on the seed's day-of-month (clamped to month length).</summary>
    Monthly,
}

/// <summary>
/// A compact RRULE-style repeat rule for a <see cref="SchedulerEvent"/>. It is expanded into
/// concrete dated occurrences for the visible window only — the model never materialises an
/// unbounded series. Each occurrence keeps the seed event's duration, colour, and category.
/// </summary>
/// <param name="Frequency">Daily, weekly, or monthly cadence.</param>
/// <param name="Interval">
/// Spacing between repeats in units of <paramref name="Frequency"/> (every 1 = each, 2 = every other…).
/// Values below 1 are treated as 1.
/// </param>
/// <param name="Count">
/// Total number of occurrences in the series counting from the seed (0 = unbounded, limited only by
/// <paramref name="Until"/> and the visible window). Occurrences are counted from the series start, so
/// scrolling the calendar never changes which dates the rule produces.
/// </param>
/// <param name="Until">Inclusive last date an occurrence may start on (null = no end date).</param>
/// <param name="ByWeekday">
/// Weekly only: the weekdays each active week repeats on (e.g. Mon/Wed/Fri). Null or empty falls back
/// to the seed event's own weekday. Ignored for daily and monthly frequencies.
/// </param>
public readonly record struct Recurrence(
    RecurrenceFrequency Frequency,
    int Interval = 1,
    int Count = 0,
    DateOnly? Until = null,
    IReadOnlyList<DayOfWeek>? ByWeekday = null);

/// <summary>A scheduled event placed on the calendar.</summary>
/// <param name="Title">Event label.</param>
/// <param name="Start">Start timestamp.</param>
/// <param name="End">
/// End timestamp. May fall on a later day than <paramref name="Start"/>; the event
/// then renders on every day in its [Start..End] date range (a multi-day month span
/// or a midnight-crossing block on each touched day column).
/// </param>
/// <param name="Color">Optional CSS color override for the event block.</param>
/// <param name="Category">
/// Optional category/status label. Conveyed as text (never color alone) so the
/// distinction survives for colour-blind and high-contrast users (WCAG 1.4.1).
/// </param>
/// <param name="Recurrence">
/// Optional repeat rule. When set, this event is a <em>seed</em>: the scheduler expands it into
/// concrete dated occurrences across the visible window (the seed itself is the first occurrence).
/// Null means a single one-off interval.
/// </param>
/// <remarks>
/// The overlap-lane layout is computed in pure C# (the planned Rust overlap-lane kernel is not
/// wired up; the date math does not need it).
/// </remarks>
public readonly record struct SchedulerEvent(
    string Title,
    DateTime Start,
    DateTime End,
    string? Color = null,
    string? Category = null,
    Recurrence? Recurrence = null);

/// <summary>An event drag-to-move result: the seed occurrence and its proposed new interval.</summary>
/// <param name="Original">The concrete occurrence the user dragged.</param>
/// <param name="NewStart">Proposed new start (the duration is preserved).</param>
/// <param name="NewEnd">Proposed new end.</param>
public readonly record struct SchedulerEventMove(SchedulerEvent Original, DateTime NewStart, DateTime NewEnd);

/// <summary>A drag-to-create result: the time range the user swept out on an empty day column.</summary>
/// <param name="Start">Proposed start.</param>
/// <param name="End">Proposed end.</param>
public readonly record struct SchedulerRange(DateTime Start, DateTime End);

/// <summary>One event laid out within a day column.</summary>
/// <param name="Event">The source event.</param>
/// <param name="DayIndex">Zero-based day column.</param>
/// <param name="OffsetHours">Hours from the view's start hour to the block's top.</param>
/// <param name="LengthHours">Block height in hours (clamped to the visible range).</param>
/// <param name="SourceIndex">
/// Index of this block's event in <see cref="SchedulerPrimitive.Events"/>, or -1 for an expanded
/// recurrence occurrence (which has no single source row). Drag-to-move keys off this: only blocks
/// with a real index are directly movable.
/// </param>
public readonly record struct ScheduledBlock(
    SchedulerEvent Event,
    int DayIndex,
    double OffsetHours,
    double LengthHours,
    int SourceIndex);

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

    /// <summary>Raised when an event is dragged to a new time slot (the host applies the move).</summary>
    [Parameter] public EventCallback<SchedulerEventMove> OnEventMoved { get; set; }

    /// <summary>Raised when the user drags out a new time range on an empty day column.</summary>
    [Parameter] public EventCallback<SchedulerRange> OnRangeCreated { get; set; }

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

    // ---- Recurrence expansion ----

    // One concrete occurrence plus a back-reference to the row it came from in Events
    // (-1 for an expanded recurrence, which is not a single editable row).
    private readonly record struct VisibleEvent(SchedulerEvent Event, int SourceIndex);

    // Bounds a recurring series so a rule whose seed is far in the past (or unbounded)
    // can never spin forever: ~11 years of daily steps is far past any visible window.
    private const int OccurrenceSafetyCap = 4000;

    // The window the styled layer can actually show, as a half-open [start, end) date range:
    // the seven (or DayCount) day columns, or the whole month grid.
    private DateOnly WindowStart => ViewStart;

    private DateOnly WindowEnd =>
        ViewStart.AddDays(View == SchedulerView.Month ? MonthWeekCount * 7 : ViewDayCount);

    /// <summary>
    /// The seed events expanded into concrete occurrences that touch the visible window. Non-recurring
    /// events pass through unchanged (with their row index); a recurring seed yields one entry per
    /// in-window occurrence (with index -1). Built lazily per render via <see cref="Visible"/>.
    /// </summary>
    private IEnumerable<VisibleEvent> ExpandVisible()
    {
        DateOnly windowStart = WindowStart;
        DateOnly windowEnd = WindowEnd;

        for (int i = 0; i < Events.Count; i++)
        {
            SchedulerEvent ev = Events[i];
            if (ev.Recurrence is null)
            {
                yield return new VisibleEvent(ev, i);
                continue;
            }

            foreach (SchedulerEvent occ in ExpandOccurrences(ev, windowStart, windowEnd))
            {
                yield return new VisibleEvent(occ, -1);
            }
        }
    }

    // Expands one recurring seed into the concrete occurrences whose [start..end] date range
    // intersects [windowStart, windowEnd). Count is measured from the seed (so paging the view
    // never shifts the series), Until is an inclusive end date, and the safety cap is the final
    // backstop. The occurrence stream is chronological, so once a start passes the window we stop.
    private IEnumerable<SchedulerEvent> ExpandOccurrences(SchedulerEvent seed, DateOnly windowStart, DateOnly windowEnd)
    {
        TimeSpan duration = seed.End - seed.Start;
        if (duration <= TimeSpan.Zero)
        {
            yield break;
        }

        Recurrence rule = seed.Recurrence!.Value;
        int produced = 0;
        int steps = 0;

        foreach (DateTime occStart in OccurrenceStarts(seed, rule))
        {
            if (++steps > OccurrenceSafetyCap)
            {
                yield break;
            }

            if (rule.Count > 0 && produced >= rule.Count)
            {
                yield break;
            }

            DateOnly occStartDay = DateOnly.FromDateTime(occStart);
            if (rule.Until is DateOnly until && occStartDay > until)
            {
                yield break;
            }

            produced++;   // counts toward Count whether or not this occurrence is on-screen

            // Past the right edge of the window: every later occurrence is later still.
            if (occStartDay >= windowEnd)
            {
                yield break;
            }

            DateTime occEnd = occStart + duration;
            if (DateOnly.FromDateTime(occEnd) < windowStart)
            {
                continue;   // ends before the window opens — not visible yet
            }

            yield return seed with { Start = occStart, End = occEnd, Recurrence = null };
        }
    }

    // The (infinite) chronological stream of occurrence start times for a rule. The caller bounds it.
    private static IEnumerable<DateTime> OccurrenceStarts(SchedulerEvent seed, Recurrence rule)
    {
        int interval = Math.Max(1, rule.Interval);
        DateTime start = seed.Start;

        if (rule.Frequency == RecurrenceFrequency.Weekly && rule.ByWeekday is { Count: > 0 })
        {
            DayOfWeek[] days = NormalizeWeekdays(rule.ByWeekday);
            DateOnly seedDay = DateOnly.FromDateTime(start);
            DateOnly anchorMonday = seedDay.AddDays(-(((int)seedDay.DayOfWeek + 6) % 7));
            TimeOnly timeOfDay = TimeOnly.FromDateTime(start);

            for (int week = 0; ; week += interval)
            {
                DateOnly weekStart = anchorMonday.AddDays(week * 7);
                foreach (DayOfWeek day in days)
                {
                    DateOnly date = weekStart.AddDays(((int)day + 6) % 7);   // Mon=0 … Sun=6
                    if (date < seedDay)
                    {
                        continue;   // skip the part of the seed's own week before it began
                    }

                    yield return date.ToDateTime(timeOfDay);
                }
            }
        }
        else
        {
            for (int n = 0; ; n++)
            {
                yield return rule.Frequency switch
                {
                    RecurrenceFrequency.Daily => start.AddDays((long)n * interval),
                    RecurrenceFrequency.Weekly => start.AddDays((long)n * 7 * interval),
                    RecurrenceFrequency.Monthly => start.AddMonths(n * interval),
                    _ => start,
                };
            }
        }
    }

    // Distinct weekdays in week order (Mon→Sun) so by-day expansion is stable and chronological.
    private static DayOfWeek[] NormalizeWeekdays(IReadOnlyList<DayOfWeek> input)
    {
        List<DayOfWeek> ordered = new(input.Count);
        foreach (DayOfWeek day in input)
        {
            if (!ordered.Contains(day))
            {
                ordered.Add(day);
            }
        }

        ordered.Sort(static (a, b) => (((int)a + 6) % 7).CompareTo(((int)b + 6) % 7));
        return ordered.ToArray();
    }

    // Per-render materialisation of ExpandVisible(): EventsOn() is called once per month cell
    // (up to 42×), so the expansion is computed once and reused rather than re-run each call.
    private List<VisibleEvent>? visibleCache;

    private IReadOnlyList<VisibleEvent> Visible => visibleCache ??= ExpandVisible().ToList();

    /// <summary>
    /// Drops the cached occurrence expansion so the next access rebuilds it. The styled layer calls
    /// this once per render before laying out, since the window or event set may have changed.
    /// </summary>
    protected void InvalidateOccurrences() => visibleCache = null;

    /// <summary>
    /// Lays every event into one block per visible day it touches (Week/Day). A
    /// multi-day or midnight-crossing event yields a separate block on each day
    /// column it overlaps, with start/end clamped to that day's visible hour range.
    /// Recurring seeds are expanded into their visible occurrences first.
    /// </summary>
    protected IEnumerable<ScheduledBlock> Blocks()
    {
        DateOnly first = ViewStart;
        int dayCount = ViewDayCount;
        foreach (VisibleEvent visible in Visible)
        {
            SchedulerEvent ev = visible.Event;
            if (ev.End <= ev.Start)
            {
                continue;
            }

            DateOnly evStartDay = DateOnly.FromDateTime(ev.Start);
            DateOnly evEndDay = DateOnly.FromDateTime(ev.End);

            // The event occupies columns for every date in [evStartDay..evEndDay].
            int firstIndex = Math.Max(0, evStartDay.DayNumber - first.DayNumber);
            int lastIndex = Math.Min(dayCount - 1, evEndDay.DayNumber - first.DayNumber);

            for (int dayIndex = firstIndex; dayIndex <= lastIndex; dayIndex++)
            {
                DateOnly day = first.AddDays(dayIndex);

                // Portion of the event that falls on this calendar day, as hours
                // from midnight: clamped to [0,24) at each day boundary it crosses.
                double startH = day == evStartDay ? ev.Start.Hour + (ev.Start.Minute / 60.0) : 0.0;
                double endH = day == evEndDay ? ev.End.Hour + (ev.End.Minute / 60.0) : 24.0;

                double top = Math.Max(StartHour, startH);
                double bottom = Math.Min(EndHour, endH);
                if (bottom <= top)
                {
                    continue;   // this day's portion is entirely outside the visible hours
                }

                yield return new ScheduledBlock(ev, dayIndex, top - StartHour, bottom - top, visible.SourceIndex);
            }
        }
    }

    /// <summary>
    /// Events that touch <paramref name="day"/> — i.e. whose [Start..End] date range
    /// includes it — ordered by start time (Month cells). A multi-day event therefore
    /// appears in every spanned cell. Recurring seeds are expanded first.
    /// </summary>
    protected IEnumerable<SchedulerEvent> EventsOn(DateOnly day)
    {
        List<SchedulerEvent> matches = new();
        foreach (VisibleEvent visible in Visible)
        {
            SchedulerEvent ev = visible.Event;
            DateOnly start = DateOnly.FromDateTime(ev.Start);
            DateOnly end = DateOnly.FromDateTime(ev.End);
            if (start <= day && day <= end)
            {
                matches.Add(ev);
            }
        }

        matches.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return matches;
    }

    protected Task SelectAsync(SchedulerEvent ev) =>
        OnEventSelected.HasDelegate ? OnEventSelected.InvokeAsync(ev) : Task.CompletedTask;

    /// <summary>
    /// Applies a drag-to-move result reported by the bridge: resolves the dragged event by its row
    /// index, moves it to <paramref name="dayIndex"/> at <paramref name="startHour"/> (preserving the
    /// duration), and raises <see cref="OnEventMoved"/>. Out-of-range indices or recurrence rows
    /// (index -1) are ignored, so a stale or malformed payload can never mutate the wrong event.
    /// </summary>
    protected Task ApplyMoveAsync(int sourceIndex, int dayIndex, double startHour)
    {
        if (sourceIndex < 0 || sourceIndex >= Events.Count || dayIndex < 0 || dayIndex >= ViewDayCount)
        {
            return Task.CompletedTask;
        }

        SchedulerEvent original = Events[sourceIndex];
        TimeSpan duration = original.End - original.Start;
        double clampedHour = Math.Clamp(startHour, StartHour, EndHour);
        DateTime newStart = WeekStart.AddDays(dayIndex).ToDateTime(TimeOnly.MinValue).AddHours(clampedHour);
        DateTime newEnd = newStart + duration;

        return OnEventMoved.HasDelegate
            ? OnEventMoved.InvokeAsync(new SchedulerEventMove(original, newStart, newEnd))
            : Task.CompletedTask;
    }

    /// <summary>
    /// Applies a drag-to-create result reported by the bridge: turns the swept day column and hour
    /// span into a concrete time range and raises <see cref="OnRangeCreated"/>. The range is clamped
    /// to the visible day and hour bounds and ordered, so an inverted or out-of-range sweep is safe.
    /// </summary>
    protected Task ApplyCreateAsync(int dayIndex, double startHour, double endHour)
    {
        if (dayIndex < 0 || dayIndex >= ViewDayCount)
        {
            return Task.CompletedTask;
        }

        double lo = Math.Clamp(Math.Min(startHour, endHour), StartHour, EndHour);
        double hi = Math.Clamp(Math.Max(startHour, endHour), StartHour, EndHour);
        if (hi <= lo)
        {
            return Task.CompletedTask;   // a zero-length sweep is a click, not a create
        }

        DateOnly day = WeekStart.AddDays(dayIndex);
        DateTime start = day.ToDateTime(TimeOnly.MinValue).AddHours(lo);
        DateTime end = day.ToDateTime(TimeOnly.MinValue).AddHours(hi);

        return OnRangeCreated.HasDelegate
            ? OnRangeCreated.InvokeAsync(new SchedulerRange(start, end))
            : Task.CompletedTask;
    }

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
    /// Applies an arrow / Home / End / PageUp / PageDown key to the active cell.
    /// Returns true when the key was a navigation key (so the caller can re-render
    /// and prevent default). Cells clamp at the grid edges (no wrap).
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
            case "PageDown":
                // Month: down one week row (ActiveRow is a week index, so +1 row =
                // +7 cells); time views: jump to the last visible hour of the day.
                ActiveRow = View == SchedulerView.Month
                    ? Math.Min(GridRows - 1, ActiveRow + 1)
                    : GridRows - 1;
                break;
            case "PageUp":
                // Month: up one week row; time views: first visible hour of the day.
                ActiveRow = View == SchedulerView.Month
                    ? Math.Max(0, ActiveRow - 1)
                    : 0;
                break;
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
