using BlazorDX.Primitives.Selection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Pure-logic tests for the selection-state primitive.</summary>
public sealed class SelectionStateTests
{
    [Fact]
    public void Single_mode_toggle_replaces_the_selection()
    {
        SelectionState<string> state = new() { Multiple = false };

        state.Toggle("a");
        Assert.True(state.IsSelected("a"));

        state.Toggle("b");
        Assert.False(state.IsSelected("a"));
        Assert.True(state.IsSelected("b"));
        Assert.Equal("b", state.Current);
    }

    [Fact]
    public void Multiple_mode_toggle_adds_and_removes()
    {
        SelectionState<int> state = new() { Multiple = true };

        state.Toggle(1);
        state.Toggle(2);
        Assert.True(state.IsSelected(1));
        Assert.True(state.IsSelected(2));

        state.Toggle(1); // remove
        Assert.False(state.IsSelected(1));
        Assert.True(state.IsSelected(2));
        Assert.Single(state.Selected);
    }

    [Fact]
    public void Set_replaces_all_and_Clear_empties()
    {
        SelectionState<string> state = new() { Multiple = true };
        state.Set(["x", "y", "z"]);
        Assert.Equal(3, state.Selected.Count);

        state.Clear();
        Assert.Empty(state.Selected);
        Assert.Null(state.Current);
    }
}
