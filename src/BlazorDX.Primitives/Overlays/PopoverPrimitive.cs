using BlazorDX.Interop;
using Microsoft.AspNetCore.Components;

namespace BlazorDX.Primitives.Overlays;

/// <summary>
/// Tier 1 headless popover: a floating panel anchored to a trigger. Composes the
/// shared primitives — anchored positioning (flip/shift), dismissal (Escape /
/// click-outside), and exit motion (in the styled layer's PresenceBoundary).
/// Unlike a dialog it does not trap focus or lock scroll. Renders no markup itself.
/// </summary>
public class PopoverPrimitive : ComponentBase, IAsyncDisposable
{
    private bool behaviorsActive;

    /// <summary>Whether the panel is open. Two-way bindable via <see cref="OpenChanged"/>.</summary>
    [Parameter] public bool Open { get; set; }

    [Parameter] public EventCallback<bool> OpenChanged { get; set; }

    /// <summary>The clickable anchor content; clicking it toggles the panel.</summary>
    [Parameter] public RenderFragment? Trigger { get; set; }

    /// <summary>The floating panel content.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Preferred side: top, bottom, left, or right (flips if there is no room).</summary>
    [Parameter] public string Side { get; set; } = "bottom";

    /// <summary>Cross-axis alignment: start, center, or end.</summary>
    [Parameter] public string Align { get; set; } = "center";

    /// <summary>Gap in pixels between the anchor and the panel.</summary>
    [Parameter] public int Offset { get; set; } = 8;

    [Parameter] public bool CloseOnEscape { get; set; } = true;

    [Parameter] public bool CloseOnOutsideClick { get; set; } = true;

    [Parameter] public int ExitDurationMs { get; set; } = 150;

    [Inject] private IOverlayInterop Overlay { get; set; } = default!;
    [Inject] private IAnchorInterop Anchor { get; set; } = default!;

    protected string AnchorId { get; } = $"dx-pop-anchor-{Guid.NewGuid():N}";
    protected string PanelId { get; } = $"dx-pop-panel-{Guid.NewGuid():N}";

    protected Task ToggleAsync() => SetOpenAsync(!Open);

    protected Task RequestCloseAsync() => SetOpenAsync(false);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Open && !behaviorsActive)
        {
            behaviorsActive = true;
            // Position first, then wire dismissal (no focus trap, no scroll lock).
            await Anchor.AttachAsync(PanelId, AnchorId, Side, Align, Offset);
            await Overlay.OpenAsync(
                PanelId, AnchorId, trapFocus: false, lockScroll: false, CloseOnEscape, CloseOnOutsideClick, OnDismiss);
        }
        else if (!Open && behaviorsActive)
        {
            behaviorsActive = false;
            await Overlay.CloseAsync(PanelId);
            await Anchor.DetachAsync(PanelId);
        }
    }

    private void OnDismiss() => _ = InvokeAsync(RequestCloseAsync);

    private async Task SetOpenAsync(bool open)
    {
        if (OpenChanged.HasDelegate)
        {
            await OpenChanged.InvokeAsync(open);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (behaviorsActive)
        {
            await Overlay.CloseAsync(PanelId);
            await Anchor.DetachAsync(PanelId);
        }
    }
}
