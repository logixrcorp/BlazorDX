using BlazorDX.Primitives.Navigation;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Pure-logic tests for the roving-tabindex keyboard-navigation primitive.</summary>
public sealed class RovingTabIndexTests
{
    [Fact]
    public void MoveNext_from_empty_selects_first_then_advances_and_wraps()
    {
        RovingTabIndex roving = new();
        roving.Configure(3);

        roving.MoveNext();
        Assert.Equal(0, roving.ActiveIndex);
        roving.MoveNext();
        Assert.Equal(1, roving.ActiveIndex);
        roving.MoveNext();
        Assert.Equal(2, roving.ActiveIndex);
        roving.MoveNext(); // wraps
        Assert.Equal(0, roving.ActiveIndex);
    }

    [Fact]
    public void MovePrevious_from_empty_selects_last()
    {
        RovingTabIndex roving = new();
        roving.Configure(3);

        roving.MovePrevious();
        Assert.Equal(2, roving.ActiveIndex);
    }

    [Fact]
    public void Navigation_skips_disabled_items()
    {
        RovingTabIndex roving = new();
        // index 1 disabled
        roving.Configure(3, index => index != 1);

        roving.MoveFirst();
        Assert.Equal(0, roving.ActiveIndex);
        roving.MoveNext();
        Assert.Equal(2, roving.ActiveIndex); // skipped 1
    }

    [Fact]
    public void Home_and_End_jump_to_enabled_bounds()
    {
        RovingTabIndex roving = new();
        roving.Configure(4, index => index != 0 && index != 3); // ends disabled

        roving.MoveFirst();
        Assert.Equal(1, roving.ActiveIndex);
        roving.MoveLast();
        Assert.Equal(2, roving.ActiveIndex);
    }

    [Fact]
    public void Clear_resets_to_inactive()
    {
        RovingTabIndex roving = new();
        roving.Configure(2);
        roving.MoveFirst();
        roving.Clear();
        Assert.Equal(-1, roving.ActiveIndex);
    }
}
