using System.Globalization;
using AngleSharp.Dom;
using BlazorDX.Components;
using BlazorDX.Primitives.Inputs;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Inline calendar: grid, single/range selection, navigation, marks, and keyboard.</summary>
public sealed class DxCalendarTests : TestContext
{
    private static readonly DateOnly June2026 = new(2026, 6, 1);

    // ---- Direct-render helper (for stateless render/CSS assertions) ----

    private IRenderedComponent<DxCalendar> Render(Action<ComponentParameterCollectionBuilder<DxCalendar>>? extra = null) =>
        RenderComponent<DxCalendar>(parameters =>
        {
            parameters.Add(c => c.Month, June2026);
            parameters.Add(c => c.Culture, CultureInfo.InvariantCulture);
            extra?.Invoke(parameters);
        });

    private static IElement Day(IRenderedFragment cal, int dayNum) =>
        cal.FindAll(".dx-cal-day:not(.dx-cal-outside)")
            .First(e => e.QuerySelector(".dx-cal-day-num")!.TextContent == dayNum.ToString(CultureInfo.InvariantCulture));

    // A stateful parent so the controlled calendar reflects selection across interactions
    // (a real consumer pushes the bound value back; bUnit's standalone render does not).
    private sealed class CalHost : ComponentBase
    {
        [Parameter] public CalendarSelectionMode Mode { get; set; } = CalendarSelectionMode.Single;
        [Parameter] public Func<DateOnly, bool>? Disabled { get; set; }

        public DateOnly? Value { get; private set; }
        public DateOnly? From { get; private set; }
        public DateOnly? To { get; private set; }
        public int RangeCompleted { get; private set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<DxCalendar>(0);
            b.AddComponentParameter(1, nameof(DxCalendar.Month), (DateOnly?)June2026);
            b.AddComponentParameter(2, nameof(DxCalendar.Culture), CultureInfo.InvariantCulture);
            b.AddComponentParameter(3, nameof(DxCalendar.SelectionMode), Mode);
            b.AddComponentParameter(4, nameof(DxCalendar.Value), Value);
            b.AddComponentParameter(5, nameof(DxCalendar.ValueChanged),
                EventCallback.Factory.Create<DateOnly?>(this, v => Value = v));
            b.AddComponentParameter(6, nameof(DxCalendar.RangeStart), From);
            b.AddComponentParameter(7, nameof(DxCalendar.RangeStartChanged),
                EventCallback.Factory.Create<DateOnly?>(this, v => From = v));
            b.AddComponentParameter(8, nameof(DxCalendar.RangeEnd), To);
            b.AddComponentParameter(9, nameof(DxCalendar.RangeEndChanged),
                EventCallback.Factory.Create<DateOnly?>(this, v => To = v));
            b.AddComponentParameter(10, nameof(DxCalendar.OnRangeSelected),
                EventCallback.Factory.Create<CalendarRange>(this, _ => RangeCompleted++));
            if (Disabled is not null)
            {
                b.AddComponentParameter(11, nameof(DxCalendar.IsDateDisabled), Disabled);
            }

            b.CloseComponent();
        }
    }

    // ---- Layout / structure ----

    [Fact]
    public void Renders_six_weeks_and_seven_weekday_headers()
    {
        IRenderedComponent<DxCalendar> cal = Render();

        Assert.Equal(42, cal.FindAll(".dx-cal-day").Count);
        Assert.Equal(7, cal.FindAll(".dx-cal-weekday").Count);
        Assert.Contains("June 2026", cal.Find(".dx-cal-month").TextContent);
    }

    [Fact]
    public void Week_starts_on_the_cultures_first_day()
    {
        // InvariantCulture's first day of week is Sunday.
        Assert.Equal("Sun", Render().FindAll(".dx-cal-weekday")[0].TextContent);
    }

    [Fact]
    public void The_month_grid_is_an_aria_grid_with_rows_and_gridcells()
    {
        IRenderedComponent<DxCalendar> cal = Render();

        Assert.NotEmpty(cal.FindAll("[role='grid']"));
        Assert.Equal(7, cal.FindAll("[role='row'] [role='columnheader']").Count);
        Assert.Equal(42, cal.FindAll(".dx-cal-week[role='row'] [role='gridcell']").Count);
    }

    [Fact]
    public void Outside_month_days_are_flagged()
    {
        // June 2026 starts on a Monday; with a Sunday-first grid the first cell (May 31) is outside.
        Assert.NotEmpty(Render().FindAll(".dx-cal-day.dx-cal-outside"));
    }

    // ---- Navigation (internal view state) ----

    [Fact]
    public void Next_and_previous_month_navigate_and_raise_MonthChanged()
    {
        DateOnly changed = default;
        IRenderedComponent<DxCalendar> cal = Render(p => p.Add(c => c.MonthChanged, m => changed = m));

        cal.Find("[aria-label='Next month']").Click();
        Assert.Equal(new DateOnly(2026, 7, 1), changed);
        Assert.Contains("July 2026", cal.Find(".dx-cal-month").TextContent);

        cal.Find("[aria-label='Previous month']").Click();
        Assert.Equal(June2026, changed);
    }

    [Fact]
    public void Today_button_jumps_to_the_current_month()
    {
        DateOnly changed = default;
        IRenderedComponent<DxCalendar> cal = Render(p => p.Add(c => c.MonthChanged, m => changed = m));

        cal.Find("[aria-label='Go to today']").Click();

        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        Assert.Equal(new DateOnly(today.Year, today.Month, 1), changed);
    }

    // ---- Single selection (stateful host) ----

    [Fact]
    public void Clicking_a_day_selects_it_in_single_mode()
    {
        IRenderedComponent<CalHost> host = RenderComponent<CalHost>();

        Day(host, 15).Click();

        Assert.Equal(new DateOnly(2026, 6, 15), host.Instance.Value);
        Assert.Contains("dx-cal-selected", Day(host, 15).ClassName);
        Assert.Equal("true", Day(host, 15).GetAttribute("aria-selected"));
    }

    // ---- Range selection (stateful host) ----

    [Fact]
    public void Range_mode_picks_start_then_end_ordered()
    {
        IRenderedComponent<CalHost> host =
            RenderComponent<CalHost>(p => p.Add(h => h.Mode, CalendarSelectionMode.Range));

        Day(host, 10).Click();
        Assert.Equal(new DateOnly(2026, 6, 10), host.Instance.From);
        Assert.Null(host.Instance.To);

        Day(host, 14).Click();
        Assert.Equal(new DateOnly(2026, 6, 10), host.Instance.From);
        Assert.Equal(new DateOnly(2026, 6, 14), host.Instance.To);
        Assert.Equal(1, host.Instance.RangeCompleted);
        Assert.Equal(5, host.FindAll(".dx-cal-in-range").Count);   // 10..14 inclusive
    }

    [Fact]
    public void Range_mode_orders_a_backwards_pick()
    {
        IRenderedComponent<CalHost> host =
            RenderComponent<CalHost>(p => p.Add(h => h.Mode, CalendarSelectionMode.Range));

        Day(host, 20).Click();   // later date first
        Day(host, 12).Click();   // earlier date second -> swaps

        Assert.Equal(new DateOnly(2026, 6, 12), host.Instance.From);
        Assert.Equal(new DateOnly(2026, 6, 20), host.Instance.To);
    }

    [Fact]
    public void A_third_click_starts_a_fresh_range()
    {
        IRenderedComponent<CalHost> host =
            RenderComponent<CalHost>(p => p.Add(h => h.Mode, CalendarSelectionMode.Range));

        Day(host, 10).Click();
        Day(host, 14).Click();   // complete
        Day(host, 20).Click();   // restart

        Assert.Equal(new DateOnly(2026, 6, 20), host.Instance.From);
        Assert.Null(host.Instance.To);
    }

    // ---- Disabled / bounds / marks (stateless) ----

    [Fact]
    public void A_disabled_date_is_flagged_and_not_clickable()
    {
        IRenderedComponent<CalHost> host = RenderComponent<CalHost>(p =>
            p.Add(h => h.Disabled, (Func<DateOnly, bool>)(d => d.Day == 15)));

        IElement cell = Day(host, 15);
        Assert.Contains("dx-cal-disabled", cell.ClassName);
        Assert.Equal("true", cell.GetAttribute("aria-disabled"));

        // A disabled cell wires no onclick, so it can never reach selection.
        Assert.Throws<Bunit.MissingEventHandlerException>(() => cell.Click());
        Assert.Null(host.Instance.Value);
    }

    [Fact]
    public void Dates_before_min_are_disabled()
    {
        IRenderedComponent<DxCalendar> cal = Render(p => p.Add(c => c.Min, new DateOnly(2026, 6, 10)));

        Assert.Contains("dx-cal-disabled", Day(cal, 5).ClassName);
        Assert.DoesNotContain("dx-cal-disabled", Day(cal, 20).ClassName);
    }

    [Fact]
    public void Marked_dates_get_a_marker_dot()
    {
        IRenderedComponent<DxCalendar> cal = Render(p =>
            p.Add(c => c.MarkedDates, new[] { new DateOnly(2026, 6, 20) }));

        IElement cell = Day(cal, 20);
        Assert.Contains("dx-cal-marked", cell.ClassName);
        Assert.NotNull(cell.QuerySelector(".dx-cal-dot"));
        Assert.Null(Day(cal, 21).QuerySelector(".dx-cal-dot"));
    }

    // ---- Keyboard ----

    [Fact]
    public void Arrow_down_moves_the_active_day_by_a_week()
    {
        IRenderedComponent<DxCalendar> cal = Render(p => p.Add(c => c.Value, new DateOnly(2026, 6, 15)));

        IElement grid = cal.Find("[role='grid']");
        Assert.EndsWith("-d-20260615", grid.GetAttribute("aria-activedescendant"));

        grid.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        grid = cal.Find("[role='grid']");
        Assert.EndsWith("-d-20260622", grid.GetAttribute("aria-activedescendant"));   // +7 days
    }

    [Fact]
    public void Enter_selects_the_active_day()
    {
        DateOnly? selected = null;
        IRenderedComponent<DxCalendar> cal = Render(p =>
        {
            p.Add(c => c.Value, new DateOnly(2026, 6, 15));
            p.Add(c => c.OnDateSelected, d => selected = d);
        });

        IElement grid = cal.Find("[role='grid']");
        grid.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });   // -> June 16
        grid.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(new DateOnly(2026, 6, 16), selected);
    }
}
