using BlazorDX.Components;
using BlazorDX.Primitives.Scheduling;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Timeline layout, clamping, progress, and selection for the Gantt chart.</summary>
public sealed class DxGanttTests : TestContext
{
    private static readonly DateOnly Start = new(2026, 6, 15);

    private static GanttTask Task(int startOffset, int endOffset, double progress = 0, string name = "T") =>
        new(name, Start.AddDays(startOffset), Start.AddDays(endOffset), progress);

    private IRenderedComponent<DxGantt> Render(params GanttTask[] tasks) =>
        RenderComponent<DxGantt>(parameters => parameters
            .Add(g => g.RangeStart, Start)
            .Add(g => g.DayCount, 10)
            .Add(g => g.DayWidth, 20)
            .Add(g => g.Tasks, tasks));

    [Fact]
    public void Renders_a_row_per_task_and_a_day_header()
    {
        IRenderedComponent<DxGantt> gantt = Render(Task(0, 2, name: "A"), Task(1, 3, name: "B"));

        Assert.Equal(2, gantt.FindAll(".dx-gantt-bar").Count);
        Assert.Equal(10, gantt.FindAll(".dx-gantt-daycol").Count);   // DayCount day labels
        Assert.Contains("A", gantt.Markup);
    }

    [Fact]
    public void Bar_is_positioned_by_start_and_inclusive_duration()
    {
        // Day 2..4 inclusive = 3 days wide, starting 2 days in.
        IRenderedComponent<DxGantt> gantt = Render(Task(2, 4));

        string style = gantt.Find(".dx-gantt-bar").GetAttribute("style")!;
        Assert.Contains("left:40px", style);    // 2 * 20
        Assert.Contains("width:60px", style);    // (4 - 2 + 1) * 20
    }

    [Fact]
    public void Bar_is_clamped_to_the_visible_window()
    {
        // Starts before the range (-3) and ends inside (day 1) -> left 0, width 2 days (0..1 inclusive).
        IRenderedComponent<DxGantt> gantt = Render(Task(-3, 1));

        string style = gantt.Find(".dx-gantt-bar").GetAttribute("style")!;
        Assert.Contains("left:0px", style);
        Assert.Contains("width:40px", style);    // days 0 and 1
    }

    [Fact]
    public void Task_entirely_outside_the_window_renders_no_bar()
    {
        IRenderedComponent<DxGantt> gantt = Render(Task(20, 25));   // beyond DayCount 10

        Assert.Empty(gantt.FindAll(".dx-gantt-bar"));
        // The name row still renders.
        Assert.Equal(2, gantt.FindAll(".dx-gantt-name").Count);     // corner + 1 task name
    }

    [Fact]
    public void Progress_fill_width_reflects_completion()
    {
        IRenderedComponent<DxGantt> gantt = Render(Task(0, 4, 0.45));

        Assert.Contains("width:45%", gantt.Find(".dx-gantt-progress").GetAttribute("style")!);
    }

    [Fact]
    public void Zero_progress_renders_no_fill()
    {
        IRenderedComponent<DxGantt> gantt = Render(Task(0, 4, 0));

        Assert.Empty(gantt.FindAll(".dx-gantt-progress"));
    }

    [Fact]
    public void Clicking_a_bar_raises_selection()
    {
        GanttTask? selected = null;
        IRenderedComponent<DxGantt> gantt = RenderComponent<DxGantt>(parameters => parameters
            .Add(g => g.RangeStart, Start)
            .Add(g => g.DayCount, 10)
            .Add(g => g.Tasks, new[] { Task(1, 3, 0.5, "Pick me") })
            .Add(g => g.OnTaskSelected, t => selected = t));

        gantt.Find(".dx-gantt-bar").Click();

        Assert.NotNull(selected);
        Assert.Equal("Pick me", selected!.Value.Name);
    }
}
