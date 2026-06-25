using BlazorDX.Components;
using BlazorDX.Primitives.Scheduling;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
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

    // ---- View switch ----

    [Fact]
    public void Renders_three_view_switch_tabs()
    {
        IRenderedComponent<DxScheduler> sched = Render();

        var tabs = sched.FindAll("[role='tab']");
        Assert.Equal(3, tabs.Count);
        Assert.Equal("Week", tabs[0].TextContent);
        Assert.Equal("Month", tabs[1].TextContent);
        Assert.Equal("Day", tabs[2].TextContent);
        Assert.Equal("true", tabs[0].GetAttribute("aria-selected"));   // Week default
    }

    [Fact]
    public void Clicking_month_tab_switches_to_month_view()
    {
        SchedulerView changed = SchedulerView.Week;
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.ViewChanged, v => changed = v));

        sched.Find("[role='tab']:nth-child(2)").Click();   // Month

        Assert.Equal(SchedulerView.Month, changed);
        Assert.NotEmpty(sched.FindAll(".dx-sched-month-cell"));
    }

    [Fact]
    public void Live_region_announces_the_active_view()
    {
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.View, SchedulerView.Month));

        var live = sched.Find(".dx-sched-sr");
        Assert.Equal("polite", live.GetAttribute("aria-live"));
        Assert.Contains("Month", live.TextContent);
    }

    // ---- Day view ----

    [Fact]
    public void Day_view_renders_a_single_day_column()
    {
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.View, SchedulerView.Day)
            .Add(s => s.StartHour, 8)
            .Add(s => s.EndHour, 18));

        Assert.Single(sched.FindAll(".dx-sched-dayhead"));
        Assert.Single(sched.FindAll(".dx-sched-col"));
        Assert.Equal(10, sched.FindAll(".dx-sched-hour").Count);
    }

    [Fact]
    public void Day_view_shows_only_that_days_events()
    {
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.View, SchedulerView.Day)
            .Add(s => s.StartHour, 8)
            .Add(s => s.EndHour, 18)
            .Add(s => s.Events, new[] { At(0, 9, 10, "Today event"), At(1, 9, 10, "Tomorrow event") }));

        var events = sched.FindAll(".dx-sched-event");
        Assert.Single(events);
        Assert.Contains("Today event", events[0].GetAttribute("aria-label"));
    }

    // ---- Month view ----

    [Fact]
    public void Month_view_renders_a_full_grid_of_cells()
    {
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)   // June 2026
            .Add(s => s.View, SchedulerView.Month));

        int cells = sched.FindAll(".dx-sched-month-cell").Count;
        Assert.True(cells % 7 == 0, "month grid is a whole number of weeks");
        Assert.InRange(cells, 28, 42);
        Assert.Equal(7, sched.FindAll(".dx-sched-month-head .dx-sched-dayhead").Count);
    }

    [Fact]
    public void Month_event_button_has_accessible_name_with_date_time_and_title()
    {
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.View, SchedulerView.Month)
            .Add(s => s.Events, new[] { At(0, 9, 10, "Sprint plan") }));

        var ev = sched.Find(".dx-sched-month-event");
        Assert.Equal("button", ev.TagName.ToLowerInvariant());
        string label = ev.GetAttribute("aria-label")!;
        Assert.Contains("Sprint plan", label);
        Assert.Contains("9:00", label);
        Assert.Contains("2026", label);   // date present
    }

    [Fact]
    public void Event_accessible_name_includes_category_text_not_colour_alone()
    {
        SchedulerEvent ev = new("Review",
            Week.ToDateTime(TimeOnly.MinValue).AddHours(9),
            Week.ToDateTime(TimeOnly.MinValue).AddHours(10),
            "#16a34a",
            "Confirmed");

        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.StartHour, 8)
            .Add(s => s.EndHour, 18)
            .Add(s => s.Events, new[] { ev }));

        var button = sched.Find(".dx-sched-event");
        Assert.Contains("Confirmed", button.GetAttribute("aria-label"));
        Assert.Contains("Confirmed", button.TextContent);   // visible text, not colour only
    }

    // ---- Keyboard navigation ----

    [Fact]
    public void Arrow_keys_move_the_active_cell_via_aria_activedescendant()
    {
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.View, SchedulerView.Month));

        var grid = sched.Find("[role='grid']");
        Assert.Null(grid.GetAttribute("aria-activedescendant"));   // none until first key

        grid.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        grid = sched.Find("[role='grid']");
        string? active = grid.GetAttribute("aria-activedescendant");
        Assert.NotNull(active);

        var cell = sched.Find($"#{active}");
        Assert.Equal("gridcell", cell.GetAttribute("role"));
    }

    [Fact]
    public void Grid_is_a_focusable_tab_stop()
    {
        IRenderedComponent<DxScheduler> sched = Render();

        var grid = sched.Find("[role='grid']");
        Assert.Equal("0", grid.GetAttribute("tabindex"));
    }
}
