using AngleSharp.Dom;
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

    // ---- Multi-day / midnight-spanning events ----

    [Fact]
    public void Multiday_event_appears_in_every_spanned_month_cell()
    {
        // Jun 1–3, 2026 spans three month cells.
        SchedulerEvent span = new("Conference",
            new DateTime(2026, 6, 1, 9, 0, 0),
            new DateTime(2026, 6, 3, 17, 0, 0));

        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)   // June 2026
            .Add(s => s.View, SchedulerView.Month)
            .Add(s => s.Events, new[] { span }));

        // The event button is rendered once per spanned day (Jun 1, 2, 3).
        var events = sched.FindAll(".dx-sched-month-event");
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Contains("Conference", e.GetAttribute("aria-label")));
    }

    [Fact]
    public void Single_day_month_event_still_appears_in_exactly_one_cell()
    {
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.View, SchedulerView.Month)
            .Add(s => s.Events, new[] { At(0, 9, 10, "Solo") }));

        Assert.Single(sched.FindAll(".dx-sched-month-event"));
    }

    [Fact]
    public void Midnight_crossing_event_appears_on_both_day_columns()
    {
        // 11pm Mon -> 1am Tue, with hours visible all day (0..24).
        SchedulerEvent overnight = new("Night shift",
            Week.ToDateTime(TimeOnly.MinValue).AddHours(23),
            Week.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(1));

        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.View, SchedulerView.Week)
            .Add(s => s.StartHour, 0)
            .Add(s => s.EndHour, 24)
            .Add(s => s.Events, new[] { overnight }));

        var cols = sched.FindAll(".dx-sched-col");
        Assert.Single(cols[0].QuerySelectorAll(".dx-sched-event"));   // Mon 23:00–24:00
        Assert.Single(cols[1].QuerySelectorAll(".dx-sched-event"));   // Tue 00:00–01:00
    }

    [Fact]
    public void Single_day_time_event_still_renders_one_block()
    {
        IRenderedComponent<DxScheduler> sched = Render(At(0, 9, 10, "Daytime"));

        Assert.Single(sched.FindAll(".dx-sched-event"));
    }

    // ---- Month grid ARIA dimensions ----

    [Fact]
    public void Month_grid_exposes_aria_col_and_row_count()
    {
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)   // June 2026
            .Add(s => s.View, SchedulerView.Month));

        var grid = sched.Find("[role='grid']");
        Assert.Equal("7", grid.GetAttribute("aria-colcount"));

        int weeks = sched.FindAll(".dx-sched-month-cell").Count / 7;
        Assert.Equal(weeks.ToString(), grid.GetAttribute("aria-rowcount"));
    }

    // ---- PageUp / PageDown / Home / End ----

    private static string CellId(IElement grid) => grid.GetAttribute("aria-activedescendant")!;

    [Fact]
    public void PageDown_in_month_moves_down_one_week_row_and_clamps()
    {
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.View, SchedulerView.Month));

        var grid = sched.Find("[role='grid']");
        grid.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });   // seeds at r0c0
        grid = sched.Find("[role='grid']");
        Assert.EndsWith("-r0c0", CellId(grid));

        grid.KeyDown(new KeyboardEventArgs { Key = "PageDown" });
        grid = sched.Find("[role='grid']");
        Assert.EndsWith("-r1c0", CellId(grid));   // moved one row down

        // PageDown repeatedly clamps at the last row (no wrap).
        for (int i = 0; i < 10; i++)
        {
            grid.KeyDown(new KeyboardEventArgs { Key = "PageDown" });
            grid = sched.Find("[role='grid']");
        }

        int weeks = sched.FindAll(".dx-sched-month-cell").Count / 7;
        Assert.EndsWith($"-r{weeks - 1}c0", CellId(grid));
    }

    [Fact]
    public void PageUp_in_month_moves_up_one_week_row_and_clamps_at_top()
    {
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.View, SchedulerView.Month));

        var grid = sched.Find("[role='grid']");
        grid.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });   // r0c0
        grid = sched.Find("[role='grid']");
        grid.KeyDown(new KeyboardEventArgs { Key = "PageDown" });    // r1c0
        grid = sched.Find("[role='grid']");
        grid.KeyDown(new KeyboardEventArgs { Key = "PageDown" });    // r2c0
        grid = sched.Find("[role='grid']");
        Assert.EndsWith("-r2c0", CellId(grid));

        grid.KeyDown(new KeyboardEventArgs { Key = "PageUp" });
        grid = sched.Find("[role='grid']");
        Assert.EndsWith("-r1c0", CellId(grid));

        // Clamp at the top row.
        grid.KeyDown(new KeyboardEventArgs { Key = "PageUp" });
        grid = sched.Find("[role='grid']");
        grid.KeyDown(new KeyboardEventArgs { Key = "PageUp" });
        grid = sched.Find("[role='grid']");
        Assert.EndsWith("-r0c0", CellId(grid));
    }

    [Fact]
    public void PageDown_PageUp_in_time_view_jump_to_last_and_first_hour()
    {
        IRenderedComponent<DxScheduler> sched = Render();   // Week, hours 8..18 => 10 rows

        var grid = sched.Find("[role='grid']");
        grid.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });   // r0c0
        grid = sched.Find("[role='grid']");

        grid.KeyDown(new KeyboardEventArgs { Key = "PageDown" });
        grid = sched.Find("[role='grid']");
        Assert.EndsWith("-r9c0", CellId(grid));   // last visible hour (row 9)

        grid.KeyDown(new KeyboardEventArgs { Key = "PageUp" });
        grid = sched.Find("[role='grid']");
        Assert.EndsWith("-r0c0", CellId(grid));   // first visible hour
    }

    [Fact]
    public void Home_and_End_move_to_row_edges_and_ctrl_to_corners()
    {
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.View, SchedulerView.Month));

        var grid = sched.Find("[role='grid']");
        grid.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });   // r0c0
        grid = sched.Find("[role='grid']");
        grid.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });  // r0c1
        grid = sched.Find("[role='grid']");
        grid.KeyDown(new KeyboardEventArgs { Key = "PageDown" });    // r1c1
        grid = sched.Find("[role='grid']");

        grid.KeyDown(new KeyboardEventArgs { Key = "End" });
        grid = sched.Find("[role='grid']");
        Assert.EndsWith("-r1c6", CellId(grid));   // last column, same row

        grid.KeyDown(new KeyboardEventArgs { Key = "Home" });
        grid = sched.Find("[role='grid']");
        Assert.EndsWith("-r1c0", CellId(grid));   // first column, same row

        grid.KeyDown(new KeyboardEventArgs { Key = "End", CtrlKey = true });
        grid = sched.Find("[role='grid']");
        int weeks = sched.FindAll(".dx-sched-month-cell").Count / 7;
        Assert.EndsWith($"-r{weeks - 1}c6", CellId(grid));   // bottom-right corner

        grid.KeyDown(new KeyboardEventArgs { Key = "Home", CtrlKey = true });
        grid = sched.Find("[role='grid']");
        Assert.EndsWith("-r0c0", CellId(grid));   // top-left corner
    }

    // ---- Empty schedule renders cleanly in every view ----

    [Theory]
    [InlineData(SchedulerView.Week)]
    [InlineData(SchedulerView.Month)]
    [InlineData(SchedulerView.Day)]
    public void Empty_schedule_renders_each_view_without_error(SchedulerView view)
    {
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, Week)
            .Add(s => s.View, view)
            .Add(s => s.Events, Array.Empty<SchedulerEvent>()));

        Assert.NotEmpty(sched.FindAll("[role='grid']"));
        Assert.Empty(sched.FindAll(".dx-sched-event"));
        Assert.Empty(sched.FindAll(".dx-sched-month-event"));
    }

    // ---- Calendar edge cases: leap February and a DST-transition month ----

    [Fact]
    public void Leap_february_2024_renders_the_right_cell_count()
    {
        // Feb 2024 is a leap month: Feb 1 2024 is a Thursday, 29 days.
        // Lead (Mon..Wed) = 3, 3 + 29 = 32 => ceil(32/7) = 5 week rows => 35 cells.
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, new DateOnly(2024, 2, 10))
            .Add(s => s.View, SchedulerView.Month));

        Assert.Equal(35, sched.FindAll(".dx-sched-month-cell").Count);
    }

    [Fact]
    public void Dst_transition_march_2026_renders_the_right_cell_count()
    {
        // Mar 2026: Mar 1 is a Sunday, 31 days. Lead (Mon..Sun) = 6, 6 + 31 = 37
        // => ceil(37/7) = 6 week rows => 42 cells. DST changes don't affect the grid.
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, new DateOnly(2026, 3, 15))
            .Add(s => s.View, SchedulerView.Month));

        Assert.Equal(42, sched.FindAll(".dx-sched-month-cell").Count);
    }

    // ---- Cross-year week range label ----

    [Fact]
    public void Week_range_label_includes_years_across_a_year_boundary()
    {
        // Week starting Mon Dec 28 2026 runs into Jan 3 2027.
        IRenderedComponent<DxScheduler> sched = RenderComponent<DxScheduler>(parameters => parameters
            .Add(s => s.WeekStart, new DateOnly(2026, 12, 28))
            .Add(s => s.View, SchedulerView.Week));

        string range = sched.Find(".dx-sched-range").TextContent;
        Assert.Contains("2026", range);
        Assert.Contains("2027", range);
        Assert.Contains("Dec 28, 2026", range);
        Assert.Contains("Jan 3, 2027", range);
    }

    [Fact]
    public void Week_range_label_omits_year_within_a_single_year()
    {
        IRenderedComponent<DxScheduler> sched = Render();   // mid-June 2026, same year

        string range = sched.Find(".dx-sched-range").TextContent;
        Assert.DoesNotContain("2026", range);
    }
}
