using BlazorDX.Primitives.Interaction;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Pure-logic tests for the list-reorder helper.</summary>
public sealed class ListReorderTests
{
    [Fact]
    public void Move_forward_shifts_intervening_items()
    {
        Assert.Equal(["b", "c", "a"], ListReorder.Move(["a", "b", "c"], 0, 2));
    }

    [Fact]
    public void Move_backward_shifts_intervening_items()
    {
        Assert.Equal(["c", "a", "b"], ListReorder.Move(["a", "b", "c"], 2, 0));
    }

    [Fact]
    public void Out_of_range_or_noop_returns_unchanged_copy()
    {
        Assert.Equal(["a", "b"], ListReorder.Move(["a", "b"], 0, 0));
        Assert.Equal(["a", "b"], ListReorder.Move(["a", "b"], 5, 0));
    }
}
