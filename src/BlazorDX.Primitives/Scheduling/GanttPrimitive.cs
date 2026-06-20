using Microsoft.AspNetCore.Components;

namespace BlazorDX.Primitives.Scheduling;

/// <summary>A task on a Gantt timeline.</summary>
/// <param name="Name">Task label.</param>
/// <param name="Start">First day (inclusive).</param>
/// <param name="End">Last day (inclusive).</param>
/// <param name="Progress">Completion fraction 0..1, shown as a fill.</param>
/// <param name="Color">Optional bar color override.</param>
public readonly record struct GanttTask(
    string Name, DateOnly Start, DateOnly End, double Progress = 0, string? Color = null);

/// <summary>A task's placement on the timeline, in day units from the range start.</summary>
/// <param name="OffsetDays">Days from the range start to the bar's left edge.</param>
/// <param name="LengthDays">Bar width in days (clamped to the visible range).</param>
public readonly record struct GanttBar(double OffsetDays, double LengthDays);

/// <summary>
/// Tier 1 headless Gantt chart. Lays each task onto a day timeline as an offset +
/// length (in days) from the range start, clamped to the visible window. Owns no
/// markup; the styled layer turns the per-task placement into positioned bars.
/// </summary>
public class GanttPrimitive : ComponentBase
{
    [Parameter] public IReadOnlyList<GanttTask> Tasks { get; set; } = [];

    /// <summary>First day shown on the timeline.</summary>
    [Parameter] public DateOnly RangeStart { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Number of days the timeline spans.</summary>
    [Parameter] public int DayCount { get; set; } = 30;

    [Parameter] public EventCallback<GanttTask> OnTaskSelected { get; set; }

    /// <summary>The day columns, left to right.</summary>
    protected IReadOnlyList<DateOnly> Days
    {
        get
        {
            DateOnly[] days = new DateOnly[Math.Max(0, DayCount)];
            for (int i = 0; i < days.Length; i++)
            {
                days[i] = RangeStart.AddDays(i);
            }

            return days;
        }
    }

    protected int TaskCount => Tasks.Count;

    protected GanttTask TaskAt(int index) => Tasks[index];

    /// <summary>The visible placement of a task, or null if it falls outside the window.</summary>
    protected GanttBar? Layout(GanttTask task)
    {
        double start = task.Start.DayNumber - RangeStart.DayNumber;
        double endExclusive = (task.End.DayNumber - RangeStart.DayNumber) + 1;   // End is inclusive

        double left = Math.Max(0, start);
        double right = Math.Min(DayCount, endExclusive);
        if (right <= left)
        {
            return null;
        }

        return new GanttBar(left, right - left);
    }

    protected static double ClampProgress(double progress) => Math.Clamp(progress, 0, 1);

    protected Task SelectAsync(GanttTask task) =>
        OnTaskSelected.HasDelegate ? OnTaskSelected.InvokeAsync(task) : Task.CompletedTask;
}
