using BlazorDX.Primitives.Charts;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Headless two-axis zoom/pan state for scatter/bubble — composes two ChartZoomPrimitive instances.</summary>
public sealed class ChartRectZoomPrimitiveTests
{
    [Fact]
    public void SetDomain_seeds_both_axes_independently()
    {
        ChartRectZoomPrimitive z = new();

        z.SetDomain(0, 100, -10, 10);

        Assert.Equal(0, z.X.DataMin);
        Assert.Equal(100, z.X.DataMax);
        Assert.Equal(-10, z.Y.DataMin);
        Assert.Equal(10, z.Y.DataMax);
        Assert.False(z.IsZoomed);
    }

    [Fact]
    public void IsZoomed_is_true_when_either_axis_alone_is_zoomed()
    {
        ChartRectZoomPrimitive z = new();
        z.SetDomain(0, 100, 0, 100);

        z.X.ZoomIn(2);
        Assert.True(z.IsZoomed);

        z.X.Reset();
        Assert.False(z.IsZoomed);

        z.Y.ZoomIn(2);
        Assert.True(z.IsZoomed);
    }

    [Fact]
    public void ZoomIn_and_ZoomOut_apply_the_same_factor_to_both_axes()
    {
        ChartRectZoomPrimitive z = new();
        z.SetDomain(0, 100, 0, 100);

        z.ZoomIn(2);

        Assert.Equal(50, z.X.VisibleSpan, precision: 6);
        Assert.Equal(50, z.Y.VisibleSpan, precision: 6);

        z.ZoomOut(2);

        Assert.Equal(100, z.X.VisibleSpan, precision: 6);
        Assert.Equal(100, z.Y.VisibleSpan, precision: 6);
    }

    [Fact]
    public void PanByFraction_moves_each_axis_independently()
    {
        ChartRectZoomPrimitive z = new();
        z.SetDomain(0, 100, 0, 100);
        z.ZoomIn(2); // both windows now span 50

        z.PanByFraction(0.1, -0.1); // X moves toward max, Y moves toward min

        Assert.True(z.X.VisibleMin > 25);
        Assert.True(z.Y.VisibleMin < 25);
    }

    [Fact]
    public void ZoomToBox_delegates_to_SetVisible_on_both_axes_with_clamping()
    {
        ChartRectZoomPrimitive z = new();
        z.SetDomain(0, 100, 0, 200);

        z.ZoomToBox(20, 60, -50, 80);

        Assert.Equal(20, z.X.VisibleMin, precision: 6);
        Assert.Equal(60, z.X.VisibleMax, precision: 6);
        Assert.Equal(0, z.Y.VisibleMin, precision: 6);   // clamped to DataMin
        Assert.Equal(80, z.Y.VisibleMax, precision: 6);
        Assert.True(z.IsZoomed);
    }

    [Fact]
    public void Reset_restores_both_axes_to_their_full_domain()
    {
        ChartRectZoomPrimitive z = new();
        z.SetDomain(0, 100, 0, 100);
        z.ZoomToBox(10, 20, 30, 40);
        Assert.True(z.IsZoomed);

        z.Reset();

        Assert.False(z.IsZoomed);
        Assert.Equal(0, z.X.VisibleMin);
        Assert.Equal(100, z.X.VisibleMax);
        Assert.Equal(0, z.Y.VisibleMin);
        Assert.Equal(100, z.Y.VisibleMax);
    }
}
