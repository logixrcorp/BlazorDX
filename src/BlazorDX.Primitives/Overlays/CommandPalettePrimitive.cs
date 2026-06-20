using BlazorDX.Interop;
using BlazorDX.Primitives.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Primitives.Overlays;

/// <summary>One command in the palette: a title and the action to run.</summary>
public sealed record Command(string Title, Action Run, string? Group = null);

/// <summary>
/// Tier 1 headless command palette (⌘K): a modal overlay with a filtering input
/// and a keyboard-navigable command list. Composes the overlay behaviors
/// (focus-trap, scroll-lock, dismissal), ComboBox-style filtering, and
/// roving-tabindex — focus stays in the input via aria-activedescendant.
/// Renders no markup itself.
/// </summary>
public class CommandPalettePrimitive : ComponentBase, IAsyncDisposable
{
    /// <summary>Roving state over the filtered commands (highlight only; focus stays in the input).</summary>
    protected readonly RovingTabIndex Roving = new();

    private List<Command> filtered = [];
    private bool behaviorsActive;
    private bool wasOpen;

    [Parameter] public IReadOnlyList<Command> Commands { get; set; } = [];

    [Parameter] public bool Open { get; set; }

    [Parameter] public EventCallback<bool> OpenChanged { get; set; }

    [Parameter] public string Placeholder { get; set; } = "Type a command...";

    [Parameter] public int ExitDurationMs { get; set; } = 150;

    [Inject] private IOverlayInterop Overlay { get; set; } = default!;

    protected string PanelId { get; } = $"dx-cmdk-{Guid.NewGuid():N}";

    protected string Filter { get; private set; } = string.Empty;

    protected IReadOnlyList<Command> Filtered => filtered;

    protected string ActiveDescendantId =>
        Roving.ActiveIndex >= 0 ? OptionId(Roving.ActiveIndex) : string.Empty;

    protected string OptionId(int index) => $"{PanelId}-cmd-{index}";

    protected bool IsActive(int index) => Roving.IsActive(index);

    protected override void OnParametersSet()
    {
        if (Open && !wasOpen)
        {
            wasOpen = true;
            Filter = string.Empty; // fresh search each time it opens
        }
        else if (!Open && wasOpen)
        {
            wasOpen = false;
        }

        ApplyFilter();
    }

    protected void OnInput(ChangeEventArgs args)
    {
        Filter = args.Value?.ToString() ?? string.Empty;
        ApplyFilter();
    }

    protected async Task OnKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowDown": Roving.MoveNext(); break;
            case "ArrowUp": Roving.MovePrevious(); break;
            case "Home": Roving.MoveFirst(); break;
            case "End": Roving.MoveLast(); break;
            case "Enter": await RunAsync(Roving.ActiveIndex); break;
        }
    }

    protected async Task RunAsync(int index)
    {
        if (index < 0 || index >= filtered.Count)
        {
            return;
        }

        filtered[index].Run();
        await CloseAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Open && !behaviorsActive)
        {
            behaviorsActive = true;
            // Modal: trap focus (lands on the input), lock scroll, dismiss on Esc/outside click.
            await Overlay.OpenAsync(
                PanelId, ignoreElementId: "", trapFocus: true, lockScroll: true, closeOnEscape: true, closeOnOutsideClick: true, OnDismiss);
        }
        else if (!Open && behaviorsActive)
        {
            behaviorsActive = false;
            await Overlay.CloseAsync(PanelId);
        }
    }

    private void ApplyFilter()
    {
        filtered = string.IsNullOrEmpty(Filter)
            ? Commands.ToList()
            : Commands.Where(command => command.Title.Contains(Filter, StringComparison.OrdinalIgnoreCase)).ToList();

        Roving.Configure(filtered.Count);
        Roving.MoveFirst();
    }

    private void OnDismiss() => _ = InvokeAsync(CloseAsync);

    private async Task CloseAsync()
    {
        if (OpenChanged.HasDelegate)
        {
            await OpenChanged.InvokeAsync(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (behaviorsActive)
        {
            await Overlay.CloseAsync(PanelId);
        }
    }
}
