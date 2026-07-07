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
            .Add(r => r.Points, new List<ChartPoint>
            {
                new(Category: "Speed", Y: 3, Series: "A"),
                new(Category: "Power", Y: 5, Series: "A"),
                new(Category: "Range", Y: 2, Series: "A"),
                new(Category: "Speed", Y: 4, Series: "B"),
                new(Category: "Power", Y: 1, Series: "B"),
                new(Category: "Range", Y: 5, Series: "B"),
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
            .Add(f => f.Points, new List<ChartPoint>
            {
                new(Category: "Visited", Y: 1000),
                new(Category: "Signed up", Y: 400),
                new(Category: "Purchased", Y: 120),
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
            .Add(c => c.Points, new List<ChartPoint>
            {
                new(Category: "Mon", Y: 10, Y2: 22, Y3: 8, Y4: 20),    // up (close >= open)
                new(Category: "Tue", Y: 20, Y2: 24, Y3: 9, Y4: 11),    // down
            }));

        Assert.Equal(2, chart.FindAll("line").Count);   // wicks
        Assert.Equal(2, chart.FindAll("rect").Count);   // bodies

        var fills = chart.FindAll("rect").Select(r => r.GetAttribute("fill")).ToList();
        Assert.Contains("#16a34a", fills);   // the up candle
        Assert.Contains("#dc2626", fills);   // the down candle
    }
}
