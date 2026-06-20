using BlazorDX.Components;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Radar, funnel, and candlestick charts — pure-SVG structure and colours.</summary>
public sealed class ChartTypesTests : TestContext
{
    [Fact]
    public void Radar_draws_rings_spokes_labels_and_a_polygon_per_series()
    {
        IRenderedComponent<DxRadarChart> radar = RenderComponent<DxRadarChart>(p => p
            .Add(r => r.Axes, new[] { "Speed", "Power", "Range" })
            .Add(r => r.Rings, 4)
            .Add(r => r.Series, new List<ChartSeries>
            {
                new("A", new double[] { 3, 5, 2 }),
                new("B", new double[] { 4, 1, 5 }),
            }));

        // 4 ring polygons + 2 series polygons.
        Assert.Equal(6, radar.FindAll("polygon").Count);
        Assert.Equal(3, radar.FindAll("line").Count);        // one spoke per axis
        Assert.Equal(3, radar.FindAll("text").Count);        // one label per axis
        Assert.Equal(2, radar.FindAll(".dx-pie-legend-item").Count);
    }

    [Fact]
    public void Funnel_draws_a_trapezoid_and_label_per_stage()
    {
        IRenderedComponent<DxFunnelChart> funnel = RenderComponent<DxFunnelChart>(p => p
            .Add(f => f.Stages, new List<ChartBar>
            {
                new("Visited", 1000),
                new("Signed up", 400),
                new("Purchased", 120),
            }));

        Assert.Equal(3, funnel.FindAll("polygon").Count);
        Assert.Equal(3, funnel.FindAll("text").Count);
        Assert.Contains("Purchased", funnel.Markup);
    }

    [Fact]
    public void Candlestick_draws_a_wick_and_body_per_candle_coloured_by_direction()
    {
        IRenderedComponent<DxCandlestickChart> chart = RenderComponent<DxCandlestickChart>(p => p
            .Add(c => c.UpColor, "#16a34a")
            .Add(c => c.DownColor, "#dc2626")
            .Add(c => c.Candles, new List<Candle>
            {
                new("Mon", 10, 22, 8, 20),    // up (close >= open)
                new("Tue", 20, 24, 9, 11),    // down
            }));

        Assert.Equal(2, chart.FindAll("line").Count);   // wicks
        Assert.Equal(2, chart.FindAll("rect").Count);   // bodies

        var fills = chart.FindAll("rect").Select(r => r.GetAttribute("fill")).ToList();
        Assert.Contains("#16a34a", fills);   // the up candle
        Assert.Contains("#dc2626", fills);   // the down candle
    }
}
