using BlazorDX.Primitives.Charts;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Headless visible-domain zoom/pan state shared by the continuous-domain charts (line, area).</summary>
public sealed class ChartZoomPrimitiveTests
{
    [Fact]
    public void SetDomain_starts_unzoomed_over_the_full_domain()
    {
        ChartZoomPrimitive z = new();

        z.SetDomain(0, 100);

        Assert.False(z.IsZoomed);
        Assert.Equal(0, z.VisibleMin);
        Assert.Equal(100, z.VisibleMax);
        Assert.Equal(0, z.DataMin);
        Assert.Equal(100, z.DataMax);
    }

    [Fact]
    public void SetDomain_collapses_a_degenerate_single_point_domain_to_a_1_unit_span()
    {
        ChartZoomPrimitive z = new();

        z.SetDomain(5, 5);

        Assert.Equal(5, z.DataMin);
        Assert.Equal(6, z.DataMax);
        Assert.False(double.IsNaN(z.VisibleSpan));
        Assert.Equal(1, z.VisibleSpan);
    }

    [Fact]
    public void ZoomAt_keeps_the_anchors_fraction_through_the_window_constant()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);

        // Anchor at 25 (25% through [0,100]); zoom in 2x -> new span 50.
        z.ZoomAt(25, 2);

        Assert.Equal(50, z.VisibleSpan, precision: 6);
        double fraction = (25 - z.VisibleMin) / z.VisibleSpan;
        Assert.Equal(0.25, fraction, precision: 6);
    }

    [Fact]
    public void ZoomAt_clamps_zoom_in_at_the_minimum_span_fraction()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);

        for (int i = 0; i < 20; i++)
        {
            z.ZoomAt(50, 2);
        }

        // Can't zoom in past 2% of the 100-unit domain.
        Assert.True(z.VisibleSpan >= 2.0 - 1e-6);
    }

    [Fact]
    public void ZoomOut_clamps_at_the_full_domain_and_IsZoomed_flips_false_exactly_there()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);
        z.ZoomAt(50, 4); // zoom in first so there's something to zoom back out of
        Assert.True(z.IsZoomed);

        for (int i = 0; i < 20; i++)
        {
            z.ZoomAt(50, 1.0 / 4);
        }

        Assert.False(z.IsZoomed);
        Assert.Equal(0, z.VisibleMin, precision: 6);
        Assert.Equal(100, z.VisibleMax, precision: 6);
    }

    [Fact]
    public void ZoomIn_and_ZoomOut_are_center_anchored()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);

        z.ZoomIn(2);

        Assert.Equal(50, z.VisibleSpan, precision: 6);
        Assert.Equal(25, z.VisibleMin, precision: 6);
        Assert.Equal(75, z.VisibleMax, precision: 6);

        z.ZoomOut(2);

        Assert.Equal(0, z.VisibleMin, precision: 6);
        Assert.Equal(100, z.VisibleMax, precision: 6);
    }

    [Fact]
    public void PanBy_shifts_the_window_without_changing_its_span()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);
        z.ZoomIn(2); // window is now [25, 75]

        z.PanBy(10);

        Assert.Equal(35, z.VisibleMin, precision: 6);
        Assert.Equal(85, z.VisibleMax, precision: 6);
        Assert.Equal(50, z.VisibleSpan, precision: 6);
    }

    [Fact]
    public void PanBy_clamps_at_the_data_min_edge()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);
        z.ZoomIn(2); // window is now [25, 75]

        z.PanBy(-1000);

        Assert.Equal(0, z.VisibleMin, precision: 6);
        Assert.Equal(50, z.VisibleMax, precision: 6); // span preserved while clamped
    }

    [Fact]
    public void PanBy_clamps_at_the_data_max_edge()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);
        z.ZoomIn(2); // window is now [25, 75]

        z.PanBy(1000);

        Assert.Equal(100, z.VisibleMax, precision: 6);
        Assert.Equal(50, z.VisibleMin, precision: 6); // span preserved while clamped
    }

    [Fact]
    public void PanByFraction_step_scales_with_the_current_visible_span()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);

        z.PanByFraction(0.1); // still at full span (100) -> would try to move 10, clamps at max edge
        Assert.Equal(100, z.VisibleMax, precision: 6);

        z.Reset();
        z.ZoomIn(10); // span now 10
        double before = z.VisibleMin;
        z.PanByFraction(0.1); // 10% of a 10-unit span = 1 unit
        Assert.Equal(before + 1, z.VisibleMin, precision: 6);
    }

    [Fact]
    public void SetDomain_preserves_the_current_zoom_window_when_the_domain_is_unchanged()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);
        z.ZoomIn(2); // window is now [25, 75]

        z.SetDomain(0, 100); // same domain again (the common re-render case)

        Assert.Equal(25, z.VisibleMin, precision: 6);
        Assert.Equal(75, z.VisibleMax, precision: 6);
    }

    [Fact]
    public void SetDomain_resets_the_zoom_window_when_the_domain_actually_changes()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);
        z.ZoomIn(2); // window is now [25, 75]

        z.SetDomain(0, 200); // genuinely new data

        Assert.False(z.IsZoomed);
        Assert.Equal(0, z.VisibleMin);
        Assert.Equal(200, z.VisibleMax);
    }

    [Fact]
    public void Reset_restores_the_full_domain_regardless_of_current_zoom_or_pan()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);
        z.ZoomIn(4);
        z.PanBy(5);

        z.Reset();

        Assert.False(z.IsZoomed);
        Assert.Equal(0, z.VisibleMin);
        Assert.Equal(100, z.VisibleMax);
    }

    [Fact]
    public void SetVisible_jumps_directly_to_an_arbitrary_window()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);

        z.SetVisible(20, 60);

        Assert.Equal(20, z.VisibleMin, precision: 6);
        Assert.Equal(60, z.VisibleMax, precision: 6);
        Assert.True(z.IsZoomed);
    }

    [Fact]
    public void SetVisible_normalizes_a_reversed_range()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);

        z.SetVisible(60, 20); // max < min, e.g. a drag that went right-to-left

        Assert.Equal(20, z.VisibleMin, precision: 6);
        Assert.Equal(60, z.VisibleMax, precision: 6);
    }

    [Fact]
    public void SetVisible_clamps_below_DataMin_and_above_DataMax()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);

        z.SetVisible(-50, 40);
        Assert.Equal(0, z.VisibleMin, precision: 6);
        Assert.Equal(40, z.VisibleMax, precision: 6);

        z.SetVisible(60, 150);
        Assert.Equal(60, z.VisibleMin, precision: 6);
        Assert.Equal(100, z.VisibleMax, precision: 6);
    }

    [Fact]
    public void SetVisible_enforces_the_minimum_span_fraction_for_a_degenerate_box()
    {
        ChartZoomPrimitive z = new();
        z.SetDomain(0, 100);

        z.SetVisible(50, 50); // a click, or a near-zero-width brush

        Assert.True(z.VisibleSpan >= 2.0 - 1e-6); // MinSpanFraction * 100
        Assert.True(z.VisibleMin <= 50 && z.VisibleMax >= 50);
    }
}
