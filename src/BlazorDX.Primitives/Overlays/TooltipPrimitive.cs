using BlazorDX.Interop;
using Microsoft.AspNetCore.Components;

namespace BlazorDX.Primitives.Overlays;

/// <summary>
/// Tier 1 headless tooltip: shows an anchored panel on hover or focus and hides it
/// on leave or blur. Reuses the anchored-positioning primitive; it neither traps
/// focus nor locks scroll nor needs click dismissal, so it does not use the overlay
/// dismiss bridge. Renders no markup itself.
/// </summary>
public class TooltipPrimitive : ComponentBase, IAsyncDisposable
{
    private bool positioned;

    [Parameter] public RenderFragment? Trigger { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string Side { get; set; } = "top";

    [Parameter] public string Align { get; set; } = "center";

    [Parameter] public int Offset { get; set; } = 6;

    [Parameter] public int ExitDurationMs { get; set; } = 120;

    [Inject] private IAnchorInterop Anchor { get; set; } = default!;

    protected string AnchorId { get; } = $"dx-tip-anchor-{Guid.NewGuid():N}";
    protected string PanelId { get; } = $"dx-tip-panel-{Guid.NewGuid():N}";

    /// <summary>Whether the tooltip is currently shown.</summary>
    protected bool Visible { get; private set; }

    protected void Show() => Visible = true;

    protected void Hide() => Visible = false;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Visible && !positioned)
        {
            positioned = true;
            await Anchor.AttachAsync(PanelId, AnchorId, Side, Align, Offset);
        }
        else if (!Visible && positioned)
        {
            positioned = false;
            await Anchor.DetachAsync(PanelId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (positioned)
        {
            await Anchor.DetachAsync(PanelId);
        }
    }
}
