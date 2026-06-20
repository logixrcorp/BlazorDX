using BlazorDX.Primitives.Selection;
using Microsoft.AspNetCore.Components;

namespace BlazorDX.Primitives.Navigation;

/// <summary>One accordion section: a title and its collapsible content.</summary>
public sealed record AccordionItem(string Title, RenderFragment Content, bool Disabled = false);

/// <summary>
/// Tier 1 headless accordion: a list of collapsible sections, single- or
/// multiple-expand, with the WAI-ARIA disclosure pattern. Composes the
/// selection-state primitive (the set of expanded indices). Renders no markup.
/// </summary>
public class AccordionPrimitive : ComponentBase
{
    private readonly SelectionState<int> expanded = new();
    private bool initialized;

    [Parameter] public IReadOnlyList<AccordionItem> Items { get; set; } = [];

    /// <summary>When true, multiple sections may be open at once.</summary>
    [Parameter] public bool Multiple { get; set; }

    /// <summary>Index expanded on first render, or -1 for all collapsed.</summary>
    [Parameter] public int InitiallyExpanded { get; set; } = -1;

    protected override void OnParametersSet()
    {
        expanded.Multiple = Multiple;
        if (!initialized)
        {
            initialized = true;
            if (InitiallyExpanded >= 0 && InitiallyExpanded < Items.Count)
            {
                expanded.Toggle(InitiallyExpanded);
            }
        }
    }

    protected string HeaderId(int index) => $"dx-acc-header-{index}";

    protected string RegionId(int index) => $"dx-acc-region-{index}";

    protected bool IsExpanded(int index) => expanded.IsSelected(index);

    /// <summary>Toggles a section open/closed (single mode collapses the others).</summary>
    protected void Toggle(int index)
    {
        if (index < 0 || index >= Items.Count || Items[index].Disabled)
        {
            return;
        }

        expanded.Toggle(index);
        StateHasChanged();
    }
}
