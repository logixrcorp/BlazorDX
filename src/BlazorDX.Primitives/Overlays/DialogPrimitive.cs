using BlazorDX.Interop;
using Microsoft.AspNetCore.Components;

namespace BlazorDX.Primitives.Overlays;

/// <summary>
/// Tier 1 headless dialog: owns open state, focus trapping, scroll locking,
/// Escape/click-outside dismissal, and the ARIA contract — but renders no markup.
/// A Tier 2 component (or a custom subclass) supplies the panel by overriding
/// <see cref="Microsoft.AspNetCore.Components.ComponentBase.BuildRenderTree"/> and
/// reading the protected members. Exit animation is delegated to a
/// <c>PresenceBoundary</c> in the styled layer.
/// </summary>
public class DialogPrimitive : ComponentBase, IAsyncDisposable
{
    // Tracks the DOM-level open state so we activate/release overlay behaviors
    // exactly once per open, independent of unrelated re-renders.
    private bool behaviorsActive;

    /// <summary>Whether the dialog is open. Two-way bindable via <see cref="OpenChanged"/>.</summary>
    [Parameter] public bool Open { get; set; }

    [Parameter] public EventCallback<bool> OpenChanged { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Accessible label when the panel has no visible heading to reference.</summary>
    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public bool CloseOnEscape { get; set; } = true;

    [Parameter] public bool CloseOnOutsideClick { get; set; } = true;

    [Parameter] public bool TrapFocus { get; set; } = true;

    [Parameter] public bool LockScroll { get; set; } = true;

    /// <summary>Milliseconds the panel stays mounted to play its exit animation.</summary>
    [Parameter] public int ExitDurationMs { get; set; } = 200;

    [Inject] private IOverlayInterop Overlay { get; set; } = default!;

    /// <summary>Stable element id for the panel, used by the overlay DOM bridge.</summary>
    protected string PanelId { get; } = $"dx-dialog-{Guid.NewGuid():N}";

    /// <summary>Asks the host to close the dialog (raises <see cref="OpenChanged"/>).</summary>
    protected async Task RequestCloseAsync()
    {
        if (OpenChanged.HasDelegate)
        {
            await OpenChanged.InvokeAsync(false);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Open && !behaviorsActive)
        {
            behaviorsActive = true;
            await Overlay.OpenAsync(
                PanelId, ignoreElementId: "", TrapFocus, LockScroll, CloseOnEscape, CloseOnOutsideClick, OnDismiss);
        }
        else if (!Open && behaviorsActive)
        {
            behaviorsActive = false;
            await Overlay.CloseAsync(PanelId);
        }
    }

    // Invoked from JS (Escape / click-outside); marshal back onto the render loop.
    private void OnDismiss() => _ = InvokeAsync(RequestCloseAsync);

    public async ValueTask DisposeAsync()
    {
        if (behaviorsActive)
        {
            await Overlay.CloseAsync(PanelId);
        }
    }
}
