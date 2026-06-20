using System.Globalization;
using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Culture-aware formatting/parsing and RTL direction.</summary>
public sealed class DxGlobalizationTests : TestContext
{
    private static readonly CultureInfo De = new("de-DE");

    public DxGlobalizationTests()
    {
        Services.AddScoped<IOverlayInterop, NullOverlayInterop>();
        Services.AddScoped<IAnchorInterop, NullAnchorInterop>();
    }

    [Fact]
    public void Numeric_formats_for_the_bound_culture()
    {
        IRenderedComponent<DxNumeric<decimal>> num = RenderComponent<DxNumeric<decimal>>(parameters => parameters
            .Add(n => n.Value, 1234.5m)
            .Add(n => n.Format, "N2")
            .Add(n => n.Culture, De));

        // de-DE: '.' groups, ',' is the decimal separator.
        Assert.Equal("1.234,50", num.Find(".dx-num-input").GetAttribute("value"));
    }

    [Fact]
    public void Numeric_parses_culture_specific_input()
    {
        decimal? bound = null;
        IRenderedComponent<DxNumeric<decimal>> num = RenderComponent<DxNumeric<decimal>>(parameters => parameters
            .Add(n => n.Culture, De)
            .Add(n => n.ValueChanged, v => bound = v));

        num.Find(".dx-num-input").Change("9,5");   // comma decimal
        Assert.Equal(9.5m, bound);
    }

    [Fact]
    public void Numeric_defaults_to_invariant_friendly_current_culture()
    {
        // Without a Culture, the invariant '.' decimal still round-trips under the
        // test host's default culture.
        decimal? bound = null;
        IRenderedComponent<DxNumeric<decimal>> num = RenderComponent<DxNumeric<decimal>>(parameters => parameters
            .Add(n => n.ValueChanged, v => bound = v));

        num.Find(".dx-num-input").Change("12.5");
        Assert.Equal(12.5m, bound);
    }

    [Fact]
    public void ThemeProvider_sets_the_text_direction()
    {
        IRenderedComponent<DxThemeProvider> theme = RenderComponent<DxThemeProvider>(parameters => parameters
            .Add(t => t.Direction, "rtl")
            .Add(t => t.ChildContent, (Microsoft.AspNetCore.Components.RenderFragment)(b => b.AddContent(0, "x"))));

        Assert.Equal("rtl", theme.Find(".dx-theme-root").GetAttribute("dir"));
    }

    [Fact]
    public void DatePicker_month_label_uses_the_bound_culture()
    {
        IRenderedComponent<DxDatePicker> picker = RenderComponent<DxDatePicker>(parameters => parameters
            .Add(p => p.Culture, De));

        picker.Find(".dx-date-trigger").Click();   // open the calendar

        DateOnly firstOfMonth = new(DateOnly.FromDateTime(DateTime.Today).Year, DateOnly.FromDateTime(DateTime.Today).Month, 1);
        string expected = firstOfMonth.ToString("MMMM yyyy", De);   // e.g. "Juni 2026"
        Assert.Equal(expected, picker.Find(".dx-date-month").TextContent);
    }
}
