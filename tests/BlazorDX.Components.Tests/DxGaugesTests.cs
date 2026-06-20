using BlazorDX.Components;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Sparkline and gauge rendering (pure SVG, no services).</summary>
public sealed class DxGaugesTests : TestContext
{
    [Fact]
    public void Sparkline_line_renders_a_polyline_with_a_point_per_value()
    {
        IRenderedComponent<DxSparkline> spark = RenderComponent<DxSparkline>(parameters => parameters
            .Add(s => s.Values, new double[] { 1, 3, 2, 5 }));

        string[] points = spark.Find("polyline").GetAttribute("points")!
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, points.Length);
    }

    [Fact]
    public void Sparkline_bar_renders_a_rect_per_value()
    {
        IRenderedComponent<DxSparkline> spark = RenderComponent<DxSparkline>(parameters => parameters
            .Add(s => s.Values, new double[] { 1, 3, 2 })
            .Add(s => s.Variant, "bar"));

        Assert.Equal(3, spark.FindAll("rect.dx-sparkline-bar").Count);
    }

    [Fact]
    public void RadialGauge_reports_meter_semantics()
    {
        IRenderedComponent<DxRadialGauge> gauge = RenderComponent<DxRadialGauge>(parameters => parameters
            .Add(g => g.Value, 72)
            .Add(g => g.Label, "CPU"));

        var svg = gauge.Find("[role=meter]");
        Assert.Equal("72", svg.GetAttribute("aria-valuenow"));
        Assert.Equal("0", svg.GetAttribute("aria-valuemin"));
        Assert.Equal("100", svg.GetAttribute("aria-valuemax"));
        Assert.Contains("72", gauge.Find(".dx-gauge-readout").TextContent);
        // Track + value arc both present.
        Assert.NotNull(gauge.Find(".dx-gauge-track").GetAttribute("d"));
        Assert.NotNull(gauge.Find(".dx-gauge-value").GetAttribute("d"));
    }

    [Fact]
    public void RadialGauge_at_minimum_draws_no_value_arc()
    {
        IRenderedComponent<DxRadialGauge> gauge = RenderComponent<DxRadialGauge>(parameters => parameters
            .Add(g => g.Value, 0));

        Assert.Empty(gauge.FindAll(".dx-gauge-value"));   // zero-length arc is omitted
    }

    [Fact]
    public void LinearGauge_fill_width_scales_with_value()
    {
        IRenderedComponent<DxLinearGauge> half = RenderComponent<DxLinearGauge>(parameters => parameters
            .Add(g => g.Value, 50).Add(g => g.Width, 200));
        IRenderedComponent<DxLinearGauge> full = RenderComponent<DxLinearGauge>(parameters => parameters
            .Add(g => g.Value, 100).Add(g => g.Width, 200));

        double halfW = double.Parse(half.Find(".dx-gauge-fill").GetAttribute("width")!,
            System.Globalization.CultureInfo.InvariantCulture);
        double fullW = double.Parse(full.Find(".dx-gauge-fill").GetAttribute("width")!,
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(100, halfW);   // 50% of 200
        Assert.Equal(200, fullW);   // 100% of 200
    }

    [Fact]
    public void LinearGauge_picks_the_zone_color_for_the_value()
    {
        IReadOnlyList<GaugeZone> zones =
        [
            new GaugeZone(50, "#16a34a"),
            new GaugeZone(80, "#d97706"),
            new GaugeZone(100, "#dc2626"),
        ];
        IRenderedComponent<DxLinearGauge> gauge = RenderComponent<DxLinearGauge>(parameters => parameters
            .Add(g => g.Value, 95)
            .Add(g => g.Zones, zones));

        // 95 > 80 -> falls in the last (danger) zone.
        Assert.Equal("#dc2626", gauge.Find(".dx-gauge-fill").GetAttribute("fill"));
    }

    [Fact]
    public void LinearGauge_reports_meter_semantics()
    {
        IRenderedComponent<DxLinearGauge> gauge = RenderComponent<DxLinearGauge>(parameters => parameters
            .Add(g => g.Value, 30)
            .Add(g => g.AriaLabel, "Memory"));

        var svg = gauge.Find("[role=meter]");
        Assert.Equal("30", svg.GetAttribute("aria-valuenow"));
        Assert.Equal("Memory", svg.GetAttribute("aria-label"));
    }
}
