using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>DxTimePicker (native time input) and DxDateRangePicker (two composed DatePickers).</summary>
public sealed class DateTimeInputTests : TestContext
{
    public DateTimeInputTests()
    {
        // DxDatePicker (used by the range picker) needs the overlay/anchor bridges.
        Services.AddScoped<IOverlayInterop, NullOverlayInterop>();
        Services.AddScoped<IAnchorInterop, NullAnchorInterop>();
    }

    [Fact]
    public void TimePicker_renders_a_time_input_and_parses_input()
    {
        TimeOnly? captured = null;
        IRenderedComponent<DxTimePicker> picker = RenderComponent<DxTimePicker>(p => p
            .Add(t => t.Value, new TimeOnly(9, 30))
            .Add(t => t.ValueChanged, EventCallback.Factory.Create<TimeOnly?>(this, v => captured = v)));

        var input = picker.Find("input.dx-input");
        Assert.Equal("time", input.GetAttribute("type"));
        Assert.Equal("09:30", input.GetAttribute("value"));

        input.Input("14:45");
        Assert.Equal(new TimeOnly(14, 45), captured);
    }

    [Fact]
    public void DateRangePicker_composes_two_date_pickers()
    {
        IRenderedComponent<DxDateRangePicker> range = RenderComponent<DxDateRangePicker>(p => p
            .Add(r => r.Start, new DateOnly(2026, 6, 10))
            .Add(r => r.End, new DateOnly(2026, 6, 20)));

        Assert.Equal(2, range.FindComponents<DxDatePicker>().Count);
        Assert.Single(range.FindAll(".dx-daterange-sep"));
    }

    [Fact]
    public async Task Picking_a_start_after_the_end_pushes_the_end_out()
    {
        DateOnly? start = null, end = null;
        IRenderedComponent<DxDateRangePicker> range = RenderComponent<DxDateRangePicker>(p => p
            .Add(r => r.Start, new DateOnly(2026, 6, 10))
            .Add(r => r.End, new DateOnly(2026, 6, 20))
            .Add(r => r.StartChanged, EventCallback.Factory.Create<DateOnly?>(this, v => start = v))
            .Add(r => r.EndChanged, EventCallback.Factory.Create<DateOnly?>(this, v => end = v)));

        DxDatePicker startPicker = range.FindComponents<DxDatePicker>()[0].Instance;
        await range.InvokeAsync(() => startPicker.ValueChanged.InvokeAsync(new DateOnly(2026, 6, 25)));

        Assert.Equal(new DateOnly(2026, 6, 25), start);
        Assert.Equal(new DateOnly(2026, 6, 25), end);   // end pushed out to keep the range valid
    }

    [Fact]
    public async Task Picking_an_end_before_the_start_is_clamped()
    {
        DateOnly? end = null;
        IRenderedComponent<DxDateRangePicker> range = RenderComponent<DxDateRangePicker>(p => p
            .Add(r => r.Start, new DateOnly(2026, 6, 10))
            .Add(r => r.End, new DateOnly(2026, 6, 20))
            .Add(r => r.EndChanged, EventCallback.Factory.Create<DateOnly?>(this, v => end = v)));

        DxDatePicker endPicker = range.FindComponents<DxDatePicker>()[1].Instance;
        await range.InvokeAsync(() => endPicker.ValueChanged.InvokeAsync(new DateOnly(2026, 6, 5)));

        Assert.Equal(new DateOnly(2026, 6, 10), end);   // clamped up to the start
    }
}
