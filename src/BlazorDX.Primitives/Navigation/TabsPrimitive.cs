using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Primitives.Navigation;

/// <summary>One tab: a title and the content shown when it is selected.</summary>
public sealed record TabItem(string Title, RenderFragment Content, bool Disabled = false);

/// <summary>
/// Tier 1 headless tabs: a roving-tabindex tab list with automatic activation
/// (arrow keys move focus and selection together) and the WAI-ARIA tab pattern.
/// Composes the roving-tabindex primitive; renders no markup itself.
/// </summary>
public class TabsPrimitive : ComponentBase
{
    /// <summary>Roving state shared with the styled layer for focus/tabindex.</summary>
    protected readonly RovingTabIndex Roving = new();

    private ElementReference[] tabElements = [];

    [Parameter] public IReadOnlyList<TabItem> Items { get; set; } = [];

    [Parameter] public int SelectedIndex { get; set; }

    [Parameter] public EventCallback<int> SelectedIndexChanged { get; set; }

    private string TablistId { get; } = $"dx-tabs-{Guid.NewGuid():N}";

    protected override void OnParametersSet()
    {
        Roving.Configure(Items.Count, index => !Items[index].Disabled);
        Roving.MoveTo(SelectedIndex); // the selected tab is the focusable one
        if (tabElements.Length != Items.Count)
        {
            tabElements = new ElementReference[Items.Count];
        }
    }

    protected string TabId(int index) => $"{TablistId}-tab-{index}";

    protected string PanelId(int index) => $"{TablistId}-panel-{index}";

    protected bool IsSelected(int index) => index == SelectedIndex;

    protected RenderFragment? SelectedContent =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex].Content : null;

    protected void CaptureTab(int index, ElementReference element)
    {
        if (index < tabElements.Length)
        {
            tabElements[index] = element;
        }
    }

    protected async Task ActivateAsync(int index)
    {
        if (index < 0 || index >= Items.Count || Items[index].Disabled || index == SelectedIndex)
        {
            return;
        }

        if (SelectedIndexChanged.HasDelegate)
        {
            await SelectedIndexChanged.InvokeAsync(index);
        }
    }

    protected async Task OnTablistKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowRight" or "ArrowDown": Roving.MoveNext(); await FocusAndActivateAsync(); break;
            case "ArrowLeft" or "ArrowUp": Roving.MovePrevious(); await FocusAndActivateAsync(); break;
            case "Home": Roving.MoveFirst(); await FocusAndActivateAsync(); break;
            case "End": Roving.MoveLast(); await FocusAndActivateAsync(); break;
        }
    }

    private async Task FocusAndActivateAsync()
    {
        int index = Roving.ActiveIndex;
        if (index >= 0 && index < tabElements.Length)
        {
            try
            {
                await tabElements[index].FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // Element not yet rendered; the next render will focus it.
            }
        }

        await ActivateAsync(index); // automatic activation
    }
}
