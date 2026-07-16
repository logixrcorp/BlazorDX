using BlazorDX.Primitives.Charts;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Headless roving-index selection/keyboard state shared by the discrete-mark charts.</summary>
public sealed class ChartSelectionPrimitiveTests
{
    [Fact]
    public void Starts_with_no_active_or_hovered_index()
    {
        ChartSelectionPrimitive s = new();

        Assert.False(s.HasActive);
        Assert.Equal(-1, s.ActiveIndex);
        Assert.Equal(-1, s.HoveredIndex);
    }

    [Theory]
    [InlineData("ArrowRight")]
    [InlineData("ArrowDown")]
    [InlineData("ArrowLeft")]
    [InlineData("ArrowUp")]
    [InlineData("Home")]
    [InlineData("End")]
    public void First_navigation_key_seeds_the_active_index_at_zero(string key)
    {
        ChartSelectionPrimitive s = new();

        bool handled = s.MoveActive(key, 5);

        Assert.True(handled);
        Assert.Equal(0, s.ActiveIndex);
    }

    [Fact]
    public void Arrow_right_and_down_move_forward_clamped_at_the_end()
    {
        ChartSelectionPrimitive s = new();
        s.MoveActive("ArrowRight", 3);   // seeds at 0
        Assert.Equal(0, s.ActiveIndex);

        s.MoveActive("ArrowRight", 3);
        Assert.Equal(1, s.ActiveIndex);

        s.MoveActive("ArrowDown", 3);    // synonym for forward
        Assert.Equal(2, s.ActiveIndex);

        s.MoveActive("ArrowRight", 3);   // clamps, no wrap
        Assert.Equal(2, s.ActiveIndex);
    }

    [Fact]
    public void Arrow_left_and_up_move_backward_clamped_at_zero()
    {
        ChartSelectionPrimitive s = new();
        s.MoveActive("End", 4);          // first key always seeds at 0 (matches Scheduler/Calendar)
        s.MoveActive("End", 4);          // now the real End: jumps to the last index
        Assert.Equal(3, s.ActiveIndex);

        s.MoveActive("ArrowLeft", 4);
        Assert.Equal(2, s.ActiveIndex);

        s.MoveActive("ArrowUp", 4);      // synonym for backward
        Assert.Equal(1, s.ActiveIndex);

        s.MoveActive("ArrowLeft", 4);
        s.MoveActive("ArrowLeft", 4);    // clamps, no wrap
        Assert.Equal(0, s.ActiveIndex);
    }

    [Fact]
    public void Home_and_End_jump_to_the_first_and_last_index()
    {
        ChartSelectionPrimitive s = new();
        s.MoveActive("Home", 5);
        Assert.Equal(0, s.ActiveIndex);

        s.MoveActive("End", 5);
        Assert.Equal(4, s.ActiveIndex);
    }

    [Fact]
    public void Unrecognized_keys_are_not_handled_and_leave_the_index_unchanged()
    {
        ChartSelectionPrimitive s = new();
        s.MoveActive("ArrowRight", 5);   // seeds at 0

        bool handled = s.MoveActive("Enter", 5);

        Assert.False(handled);
        Assert.Equal(0, s.ActiveIndex);
    }

    [Fact]
    public void MoveActive_is_a_no_op_when_count_is_zero()
    {
        ChartSelectionPrimitive s = new();

        Assert.False(s.MoveActive("ArrowRight", 0));
        Assert.False(s.HasActive);
    }

    [Fact]
    public void SetActive_ignores_an_out_of_range_index()
    {
        ChartSelectionPrimitive s = new();

        s.SetActive(10, 3);

        Assert.False(s.HasActive);
    }

    [Fact]
    public void SetActive_sets_a_valid_index_directly()
    {
        ChartSelectionPrimitive s = new();

        s.SetActive(2, 5);

        Assert.True(s.IsActive(2));
        Assert.False(s.IsActive(1));
    }

    [Fact]
    public void ClampTo_re_anchors_the_active_index_when_the_count_shrinks()
    {
        ChartSelectionPrimitive s = new();
        s.SetActive(4, 5);

        s.ClampTo(2);

        Assert.Equal(1, s.ActiveIndex);   // clamped to the new last index
    }

    [Fact]
    public void ClampTo_clears_the_active_index_when_the_count_drops_to_zero()
    {
        ChartSelectionPrimitive s = new();
        s.SetActive(1, 3);

        s.ClampTo(0);

        Assert.False(s.HasActive);
    }

    [Fact]
    public void SetHovered_tracks_the_hovered_index_independently_of_active()
    {
        ChartSelectionPrimitive s = new();
        s.SetActive(0, 3);

        s.SetHovered(2);

        Assert.True(s.IsHovered(2));
        Assert.True(s.IsActive(0));
        Assert.False(s.IsHovered(0));
    }
}
