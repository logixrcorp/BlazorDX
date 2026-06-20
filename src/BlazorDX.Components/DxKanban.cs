using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>A card on a <see cref="DxKanban"/> board (a class for reference identity).</summary>
public sealed class KanbanCard
{
    public KanbanCard(string title, string? tag = null)
    {
        Title = title;
        Tag = tag;
    }

    public string Title { get; set; }

    public string? Tag { get; set; }
}

/// <summary>A column on a <see cref="DxKanban"/> board.</summary>
public sealed class KanbanColumn
{
    public KanbanColumn(string title, IEnumerable<KanbanCard>? cards = null)
    {
        Title = title;
        Cards = cards is null ? new List<KanbanCard>() : new List<KanbanCard>(cards);
    }

    public string Title { get; set; }

    public List<KanbanCard> Cards { get; set; }
}

/// <summary>
/// A Kanban board: columns of cards that move by drag-and-drop (HTML5 native drag — no
/// JS) across and within columns, or by keyboard (Alt+Up/Down to reorder within a
/// column, Ctrl+Left/Right to move to an adjacent column), with focus following the
/// moved card. Raises <see cref="OnChange"/> after any move. Styling via dx-display.css.
/// </summary>
public sealed class DxKanban : ComponentBase
{
    /// <summary>The board columns (mutated in place as cards move).</summary>
    [Parameter, EditorRequired] public IReadOnlyList<KanbanColumn> Columns { get; set; } = [];

    /// <summary>Raised after a card is moved.</summary>
    [Parameter] public EventCallback OnChange { get; set; }

    /// <summary>Extra CSS classes appended to the board.</summary>
    [Parameter] public string? Class { get; set; }

    private KanbanCard? dragged;
    private KanbanColumn? draggedFrom;
    private KanbanCard? pendingFocus;
    private readonly Dictionary<KanbanCard, ElementReference> elements = new(ReferenceEqualityComparer.Instance);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        elements.Clear();
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-kanban {Class}".TrimEnd());

        foreach (KanbanColumn column in Columns)
        {
            KanbanColumn col = column;
            builder.OpenElement(2, "section");
            builder.SetKey(column);
            builder.AddAttribute(3, "class", "dx-kanban-column");
            builder.AddAttribute(4, "aria-label", column.Title);

            builder.OpenElement(5, "header");
            builder.AddAttribute(6, "class", "dx-kanban-col-head");
            builder.AddContent(7, column.Title);
            builder.OpenElement(8, "span");
            builder.AddAttribute(9, "class", "dx-kanban-count");
            builder.AddContent(10, column.Cards.Count);
            builder.CloseElement();
            builder.CloseElement();

            // The column body is a drop target (drop appends to the end).
            builder.OpenElement(11, "div");
            builder.AddAttribute(12, "class", "dx-kanban-col-body");
            builder.AddEventPreventDefaultAttribute(13, "ondragover", true);
            builder.AddAttribute(14, "ondrop", EventCallback.Factory.Create(this, () => DropOnColumnAsync(col)));

            foreach (KanbanCard card in column.Cards)
            {
                BuildCard(builder, card, col);
            }

            builder.CloseElement();
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildCard(RenderTreeBuilder builder, KanbanCard card, KanbanColumn column)
    {
        KanbanCard captured = card;
        builder.OpenElement(15, "div");
        builder.SetKey(card);
        builder.AddAttribute(16, "class", "dx-kanban-card");
        builder.AddAttribute(17, "draggable", "true");
        builder.AddAttribute(18, "tabindex", "0");
        builder.AddAttribute(19, "ondragstart", EventCallback.Factory.Create(this, () => Start(captured, column)));
        builder.AddEventPreventDefaultAttribute(20, "ondragover", true);
        builder.AddAttribute(21, "ondrop", EventCallback.Factory.Create(this, () => DropOnCardAsync(captured, column)));
        builder.AddEventStopPropagationAttribute(22, "ondrop", true);   // don't also fire the column drop
        builder.AddAttribute(23, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, args => OnKeyAsync(args, captured, column)));
        builder.AddElementReferenceCapture(24, element => elements[captured] = element);

        builder.OpenElement(25, "span");
        builder.AddAttribute(26, "class", "dx-kanban-card-title");
        builder.AddContent(27, card.Title);
        builder.CloseElement();

        if (card.Tag is not null)
        {
            builder.OpenElement(28, "span");
            builder.AddAttribute(29, "class", "dx-kanban-card-tag");
            builder.AddContent(30, card.Tag);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void Start(KanbanCard card, KanbanColumn from)
    {
        dragged = card;
        draggedFrom = from;
    }

    private Task DropOnColumnAsync(KanbanColumn target) => MoveAsync(dragged, draggedFrom, target, before: null);

    private Task DropOnCardAsync(KanbanCard before, KanbanColumn target) => MoveAsync(dragged, draggedFrom, target, before);

    private async Task MoveAsync(KanbanCard? card, KanbanColumn? from, KanbanColumn target, KanbanCard? before)
    {
        if (card is null || from is null || ReferenceEquals(card, before))
        {
            return;
        }

        from.Cards.Remove(card);
        int index = before is null ? target.Cards.Count : target.Cards.IndexOf(before);
        if (index < 0)
        {
            index = target.Cards.Count;
        }

        target.Cards.Insert(index, card);
        dragged = null;
        draggedFrom = null;
        await NotifyAsync();
    }

    private async Task OnKeyAsync(KeyboardEventArgs args, KanbanCard card, KanbanColumn column)
    {
        // Alt+Up/Down reorders within the column; Ctrl+Left/Right moves across columns.
        if (args.AltKey && args.Key is "ArrowUp" or "ArrowDown")
        {
            int index = column.Cards.IndexOf(card);
            int next = index + (args.Key == "ArrowUp" ? -1 : 1);
            if (next >= 0 && next < column.Cards.Count)
            {
                column.Cards.RemoveAt(index);
                column.Cards.Insert(next, card);
                pendingFocus = card;
                await NotifyAsync();
            }
        }
        else if (args.CtrlKey && args.Key is "ArrowLeft" or "ArrowRight")
        {
            int ci = IndexOfColumn(column);
            int ti = ci + (args.Key == "ArrowLeft" ? -1 : 1);
            if (ci >= 0 && ti >= 0 && ti < Columns.Count)
            {
                column.Cards.Remove(card);
                Columns[ti].Cards.Add(card);
                pendingFocus = card;
                await NotifyAsync();
            }
        }
    }

    private int IndexOfColumn(KanbanColumn column)
    {
        for (int i = 0; i < Columns.Count; i++)
        {
            if (ReferenceEquals(Columns[i], column))
            {
                return i;
            }
        }

        return -1;
    }

    private async Task NotifyAsync()
    {
        StateHasChanged();
        if (OnChange.HasDelegate)
        {
            await OnChange.InvokeAsync();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (pendingFocus is not null && elements.TryGetValue(pendingFocus, out ElementReference reference))
        {
            pendingFocus = null;
            try
            {
                await reference.FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // Card no longer in the DOM; ignore.
            }
        }
    }
}
