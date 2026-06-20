using System.Globalization;
using BlazorDX.Primitives.Scheduling;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled week scheduler built on <see cref="SchedulerPrimitive"/>. Renders
/// a toolbar, day-column headers, an hour grid, and each event as an absolutely
/// positioned block within its day column. Styling is token-driven (see
/// dx-scheduler.css).
/// </summary>
public sealed class DxScheduler : SchedulerPrimitive
{
    /// <summary>Pixel height of one hour row.</summary>
    [Parameter] public int HourHeight { get; set; } = 44;

    [Parameter] public string? Class { get; set; }

    private string Columns => $"grid-template-columns:56px repeat({DayCount},minmax(0,1fr));";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-sched {Class}".TrimEnd());

        BuildToolbar(builder);
        BuildHead(builder);
        BuildBody(builder);

        builder.CloseElement();
    }

    private void BuildToolbar(RenderTreeBuilder builder)
    {
        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-sched-toolbar");

        NavButton(builder, 4, "‹", "Previous week", PreviousWeekAsync);
        NavButton(builder, 8, "Today", "Go to today", TodayAsync);
        NavButton(builder, 12, "›", "Next week", NextWeekAsync);

        builder.OpenElement(16, "span");
        builder.AddAttribute(17, "class", "dx-sched-range");
        DateOnly last = WeekStart.AddDays(Math.Max(0, DayCount - 1));
        builder.AddContent(18,
            $"{WeekStart.ToString("MMM d", CultureInfo.InvariantCulture)} – {last.ToString("MMM d", CultureInfo.InvariantCulture)}");
        builder.CloseElement();

        builder.CloseElement();
    }

    private void NavButton(RenderTreeBuilder builder, int seq, string text, string label, Func<Task> onClick)
    {
        builder.OpenElement(seq, "button");
        builder.AddAttribute(seq + 1, "type", "button");
        builder.AddAttribute(seq + 2, "class", "dx-sched-nav");
        builder.AddAttribute(seq + 3, "aria-label", label);
        builder.AddAttribute(seq + 4, "onclick", EventCallback.Factory.Create(this, onClick));
        builder.AddContent(seq + 5, text);
        builder.CloseElement();
    }

    private void BuildHead(RenderTreeBuilder builder)
    {
        builder.OpenElement(20, "div");
        builder.AddAttribute(21, "class", "dx-sched-head");
        builder.AddAttribute(22, "style", Columns);

        builder.OpenElement(23, "div");
        builder.AddAttribute(24, "class", "dx-sched-corner");
        builder.CloseElement();

        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        foreach (DateOnly day in Days)
        {
            builder.OpenElement(25, "div");
            builder.SetKey(day);
            builder.AddAttribute(26, "class", day == today ? "dx-sched-dayhead dx-sched-today" : "dx-sched-dayhead");
            builder.AddContent(27, $"{day.DayOfWeek.ToString()[..3]} {day.Day}");
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildBody(RenderTreeBuilder builder)
    {
        int bodyHeight = HourSpan * HourHeight;

        builder.OpenElement(28, "div");
        builder.AddAttribute(29, "class", "dx-sched-body");
        builder.AddAttribute(30, "style", Columns);

        // Time axis.
        builder.OpenElement(31, "div");
        builder.AddAttribute(32, "class", "dx-sched-axis");
        foreach (int hour in Hours)
        {
            builder.OpenElement(33, "div");
            builder.SetKey(hour);
            builder.AddAttribute(34, "class", "dx-sched-hour");
            builder.AddAttribute(35, "style", $"height:{HourHeight}px;");
            builder.AddContent(36, $"{hour}:00");
            builder.CloseElement();
        }

        builder.CloseElement();

        // Day columns with positioned event blocks.
        ScheduledBlock[] blocks = Blocks().ToArray();
        for (int dayIndex = 0; dayIndex < DayCount; dayIndex++)
        {
            int captured = dayIndex;
            builder.OpenElement(37, "div");
            builder.SetKey(dayIndex);
            builder.AddAttribute(38, "class", "dx-sched-col");
            builder.AddAttribute(39, "style",
                $"height:{bodyHeight}px;background-size:100% {HourHeight}px;");

            foreach (ScheduledBlock block in blocks)
            {
                if (block.DayIndex == captured)
                {
                    BuildEvent(builder, block);
                }
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildEvent(RenderTreeBuilder builder, ScheduledBlock block)
    {
        double top = block.OffsetHours * HourHeight;
        double height = block.LengthHours * HourHeight;
        SchedulerEvent ev = block.Event;

        builder.OpenElement(40, "button");
        builder.AddAttribute(41, "type", "button");
        builder.AddAttribute(42, "class", "dx-sched-event");
        builder.AddAttribute(43, "style",
            string.Create(CultureInfo.InvariantCulture,
                $"top:{top:0.#}px;height:{height:0.#}px;{(ev.Color is null ? string.Empty : $"background:{ev.Color};")}"));
        builder.AddAttribute(44, "aria-label",
            $"{ev.Title}, {ev.Start.ToString("t", CultureInfo.InvariantCulture)} to {ev.End.ToString("t", CultureInfo.InvariantCulture)}");
        builder.AddAttribute(45, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(ev)));

        builder.OpenElement(46, "span");
        builder.AddAttribute(47, "class", "dx-sched-event-time");
        builder.AddContent(48, ev.Start.ToString("t", CultureInfo.InvariantCulture));
        builder.CloseElement();

        builder.OpenElement(49, "span");
        builder.AddAttribute(50, "class", "dx-sched-event-title");
        builder.AddContent(51, ev.Title);
        builder.CloseElement();

        builder.CloseElement();
    }
}
