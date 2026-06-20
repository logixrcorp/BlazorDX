using System.Globalization;
using BlazorDX.Primitives.Scheduling;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled Gantt chart built on <see cref="GanttPrimitive"/>. A fixed task-
/// name column sits beside a horizontally-scrolling day timeline; each task renders
/// as a bar (with a progress fill) positioned by the inherited day layout. Styling
/// is token-driven (see dx-scheduler.css).
/// </summary>
public sealed class DxGantt : GanttPrimitive
{
    /// <summary>Pixel width of one day column.</summary>
    [Parameter] public int DayWidth { get; set; } = 28;

    /// <summary>Pixel height of one task row.</summary>
    [Parameter] public int RowHeight { get; set; } = 34;

    /// <summary>Width of the task-name column in pixels.</summary>
    [Parameter] public int NameWidth { get; set; } = 160;

    [Parameter] public string? Class { get; set; }

    private int TimelineWidth => DayCount * DayWidth;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-gantt {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "table");
        builder.AddAttribute(3, "style", $"--dx-gantt-name:{NameWidth}px;");

        BuildHeader(builder);
        for (int i = 0; i < TaskCount; i++)
        {
            BuildRow(builder, i);
        }

        builder.CloseElement();
    }

    private void BuildHeader(RenderTreeBuilder builder)
    {
        builder.OpenElement(4, "div");
        builder.AddAttribute(5, "class", "dx-gantt-row dx-gantt-headrow");

        builder.OpenElement(6, "div");
        builder.AddAttribute(7, "class", "dx-gantt-name dx-gantt-corner");
        builder.AddContent(8, "Task");
        builder.CloseElement();

        builder.OpenElement(9, "div");
        builder.AddAttribute(10, "class", "dx-gantt-track");
        builder.AddAttribute(11, "style", $"width:{TimelineWidth}px;");

        foreach (DateOnly day in Days)
        {
            bool weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            builder.OpenElement(12, "div");
            builder.SetKey(day);
            builder.AddAttribute(13, "class", weekend ? "dx-gantt-daycol dx-gantt-weekend" : "dx-gantt-daycol");
            builder.AddAttribute(14, "style", $"width:{DayWidth}px;");
            builder.AddContent(15, day.Day);
            builder.CloseElement();
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private void BuildRow(RenderTreeBuilder builder, int index)
    {
        GanttTask task = TaskAt(index);

        builder.OpenElement(16, "div");
        builder.SetKey(index);
        builder.AddAttribute(17, "class", "dx-gantt-row");
        builder.AddAttribute(18, "style", $"height:{RowHeight}px;");

        builder.OpenElement(19, "div");
        builder.AddAttribute(20, "class", "dx-gantt-name");
        builder.AddContent(21, task.Name);
        builder.CloseElement();

        builder.OpenElement(22, "div");
        builder.AddAttribute(23, "class", "dx-gantt-track");
        builder.AddAttribute(24, "style", $"width:{TimelineWidth}px;background-size:{DayWidth}px 100%;");

        if (Layout(task) is GanttBar bar)
        {
            BuildBar(builder, task, bar);
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private void BuildBar(RenderTreeBuilder builder, GanttTask task, GanttBar bar)
    {
        double left = bar.OffsetDays * DayWidth;
        double width = bar.LengthDays * DayWidth;
        double progress = ClampProgress(task.Progress);

        builder.OpenElement(25, "button");
        builder.AddAttribute(26, "type", "button");
        builder.AddAttribute(27, "class", "dx-gantt-bar");
        builder.AddAttribute(28, "style",
            string.Create(CultureInfo.InvariantCulture,
                $"left:{left:0.#}px;width:{width:0.#}px;{(task.Color is null ? string.Empty : $"background:{task.Color};")}"));
        builder.AddAttribute(29, "aria-label",
            $"{task.Name}, {task.Start.ToString("MMM d", CultureInfo.InvariantCulture)} to {task.End.ToString("MMM d", CultureInfo.InvariantCulture)}, {progress * 100:0}% complete");
        builder.AddAttribute(30, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(task)));

        if (progress > 0)
        {
            builder.OpenElement(31, "span");
            builder.AddAttribute(32, "class", "dx-gantt-progress");
            builder.AddAttribute(33, "style",
                string.Create(CultureInfo.InvariantCulture, $"width:{progress * 100:0.#}%;"));
            builder.CloseElement();
        }

        builder.OpenElement(34, "span");
        builder.AddAttribute(35, "class", "dx-gantt-bar-label");
        builder.AddContent(36, task.Name);
        builder.CloseElement();

        builder.CloseElement();
    }
}
