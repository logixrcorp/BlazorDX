using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>DxKanban: drag a card between columns; keyboard reorder within / move across.</summary>
public sealed class KanbanTests : TestContext
{
    private static List<KanbanColumn> Board() =>
    [
        new("To do", new[] { new KanbanCard("A"), new KanbanCard("B"), new KanbanCard("C") }),
        new("Doing", new[] { new KanbanCard("X") }),
    ];

    private static int CardCount(IRenderedComponent<DxKanban> board, int column) =>
        board.FindAll(".dx-kanban-column")[column].QuerySelectorAll(".dx-kanban-card").Length;

    [Fact]
    public void Dragging_a_card_onto_another_column_moves_it()
    {
        int changes = 0;
        List<KanbanColumn> cols = Board();
        IRenderedComponent<DxKanban> board = RenderComponent<DxKanban>(p => p
            .Add(k => k.Columns, cols)
            .Add(k => k.OnChange, EventCallback.Factory.Create(this, () => changes++)));

        Assert.Equal(3, CardCount(board, 0));
        Assert.Equal(1, CardCount(board, 1));

        board.FindAll(".dx-kanban-card")[0].DragStart();          // "A" in column 0
        board.FindAll(".dx-kanban-col-body")[1].Drop();           // drop on column 1's body

        Assert.Equal(2, CardCount(board, 0));
        Assert.Equal(2, CardCount(board, 1));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Alt_arrow_reorders_within_a_column()
    {
        IRenderedComponent<DxKanban> board = RenderComponent<DxKanban>(p => p.Add(k => k.Columns, Board()));

        // Move "A" (first card of column 0) down → order becomes B, A, C.
        board.FindAll(".dx-kanban-column")[0].QuerySelectorAll(".dx-kanban-card")[0]
            .KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        var titles = board.FindAll(".dx-kanban-column")[0]
            .QuerySelectorAll(".dx-kanban-card-title").Select(e => e.TextContent).ToArray();
        Assert.Equal(new[] { "B", "A", "C" }, titles);
    }

    [Fact]
    public void Ctrl_arrow_moves_a_card_to_the_adjacent_column()
    {
        IRenderedComponent<DxKanban> board = RenderComponent<DxKanban>(p => p.Add(k => k.Columns, Board()));

        board.FindAll(".dx-kanban-column")[0].QuerySelectorAll(".dx-kanban-card")[0]
            .KeyDown(new KeyboardEventArgs { Key = "ArrowRight", CtrlKey = true });

        Assert.Equal(2, CardCount(board, 0));
        Assert.Equal(2, CardCount(board, 1));
    }
}
