using Microsoft.AspNetCore.Components;

namespace BlazorDX.Primitives.Scheduling;

/// <summary>A scheduled event placed on the calendar.</summary>
/// <param name="Title">Event label.</param>
/// <param name="Start">Start timestamp.</param>
/// <param name="End">End timestamp (same day as <paramref name="Start"/> for this view).</param>
/// <param name="Color">Optional CSS color override for the event block.</param>
public readonly record struct SchedulerEvent(string Title, DateTime Start, DateTime End, string? Color = null);

/// <summary>One event laid out within a day column.</summary>
/// <param name="Event">The source event.</param>
/// <param name="DayIndex">Zero-based day column.</param>
/// <param name="OffsetHours">Hours from the view's start hour to the block's top.</param>
/// <param name="LengthHours">Block height in hours (clamped to the visible range).</param>
public readonly record struct ScheduledBlock(SchedulerEvent Event, int DayIndex, double OffsetHours, double LengthHours);

/// <summary>
/// Tier 1 headless week scheduler. Computes the visible day columns and lays each
/// event out as a <see cref="ScheduledBlock"/> (day column + vertical offset and
/// height in hours, clamped to the visible hour range). Owns week navigation but
/// renders nothing; the styled layer turns blocks into positioned markup.
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

    [Parameter] public EventCallback<SchedulerEvent> OnEventSelected { get; set; }

    /// <summary>The day columns, left to right.</summary>
    protected IReadOnlyList<DateOnly> Days
    {
        get
        {
            DateOnly[] days = new DateOnly[Math.Max(0, DayCount)];
            for (int i = 0; i < days.Length; i++)
            {
                days[i] = WeekStart.AddDays(i);
            }

            return days;
        }
    }

    /// <summary>Hour labels down the time axis (start..end, inclusive of the start of each hour).</summary>
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

    /// <summary>Lays every event that falls within the visible week and hour range into a block.</summary>
    protected IEnumerable<ScheduledBlock> Blocks()
    {
        foreach (SchedulerEvent ev in Events)
        {
            int dayIndex = (DateOnly.FromDateTime(ev.Start).DayNumber - WeekStart.DayNumber);
            if (dayIndex < 0 || dayIndex >= DayCount)
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

    protected Task SelectAsync(SchedulerEvent ev) =>
        OnEventSelected.HasDelegate ? OnEventSelected.InvokeAsync(ev) : Task.CompletedTask;

    protected Task PreviousWeekAsync() => GoToAsync(WeekStart.AddDays(-DayCount));

    protected Task NextWeekAsync() => GoToAsync(WeekStart.AddDays(DayCount));

    protected Task TodayAsync() => GoToAsync(DateOnly.FromDateTime(DateTime.Today));

    private Task GoToAsync(DateOnly start)
    {
        WeekStart = start;
        return WeekStartChanged.HasDelegate ? WeekStartChanged.InvokeAsync(start) : Task.CompletedTask;
    }
}
