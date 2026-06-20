using BlazorDX.Components;
using BlazorDX.Primitives.Scheduling;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Week layout, event positioning, and navigation for the scheduler.</summary>
public sealed class DxSchedulerTests : TestContext
{
    private static readonly DateOnly Week = new(2026, 6, 15);

    private static SchedulerEvent At(int dayOffset, double startHour, double endHour, string title = "E") =>
        new(title,
            Week.AddDays(dayOffset).ToDateTime(TimeOnly.MinValue).AddHours(startHour),
            Week.AddDays(dayOffset).ToDateTime(TimeOnly.MinValue).AddHours(endHour));

    private IRenderedComponent<DxScheduler> Render(params SchedulerEvent[] events) =>
        RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.Events, events)
            .Add(s => s.StartHour, 8)
            .Add(s => s.EndHour, 18)
            .Add(s => s.HourHeight, 44));

    [Fact]
    public void Renders_day_headers_and_hour_axis()
    {
        IRenderedComponent<DxScheduler> sched = Render();

        Assert.Equal(7, sched.FindAll(".dx-sched-dayhead").Count);   // default 7-day week
        Assert.Equal(10, sched.FindAll(".dx-sched-hour").Count);     // 8..17
    }

    [Fact]
    public void Event_is_positioned_by_start_time_and_duration()
    {
        IRenderedComponent<DxScheduler> sched = Render(At(0, 9, 9.5));

        var ev = sched.Find(".dx-sched-event");
        string style = ev.GetAttribute("style")!;
        Assert.Contains("top:44px", style);     // (9 - 8) * 44
        Assert.Contains("height:22px", style);   // 0.5 * 44
    }

    [Fact]
    public void Event_lands_in_the_correct_day_column()
    {
        IRenderedComponent<DxScheduler> sched = Render(At(2, 14, 15, "Wed event"));

        var cols = sched.FindAll(".dx-sched-col");
        Assert.Empty(cols[0].QuerySelectorAll(".dx-sched-event"));
        Assert.Single(cols[2].QuerySelectorAll(".dx-sched-event"));
        Assert.Contains("Wed event", cols[2].QuerySelectorAll(".dx-sched-event")[0].TextContent);
    }

    [Fact]
    public void Events_outside_the_visible_hours_are_dropped()
    {
        IRenderedComponent<DxScheduler> sched = Render(At(0, 5, 6));   // 5am, before StartHour 8

        Assert.Empty(sched.FindAll(".dx-sched-event"));
    }

    [Fact]
    public void Next_week_advances_the_week_start()
    {
        DateOnly changed = default;
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.WeekStartChanged, d => changed = d));

        sched.Find("[aria-label='Next week']").Click();

        Assert.Equal(Week.AddDays(7), changed);
    }

    [Fact]
    public void Clicking_an_event_raises_selection()
    {
        SchedulerEvent? selected = null;
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.StartHour, 8)
            .Add(s => s.EndHour, 18)
            .Add(s => s.Events, new[] { At(1, 10, 11, "Pick me") })
            .Add(s => s.OnEventSelected, e => selected = e));

        sched.Find(".dx-sched-event").Click();

        Assert.NotNull(selected);
        Assert.Equal("Pick me", selected!.Value.Title);
    }
}
