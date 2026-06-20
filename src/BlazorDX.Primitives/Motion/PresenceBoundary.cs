using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Primitives.Motion;

/// <summary>
/// Delays a child's removal from the DOM so it can animate out — the piece
/// Blazor lacks natively (an <c>@if</c> that turns false destroys the node at
/// once). When <see cref="Visible"/> flips to false, the boundary keeps the child
/// mounted, swaps in <see cref="LeaveClass"/> to trigger the CSS exit transition,
/// waits <see cref="ExitDurationMs"/>, then releases it. This is the
/// AnimatePresence equivalent described in ADR 0005.
/// </summary>
public sealed class PresenceBoundary : ComponentBase
{
    private bool present;
    private string stateClass = string.Empty;

    [Parameter] public bool Visible { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>CSS class applied while the child is entering/visible.</summary>
    [Parameter] public string EnterClass { get; set; } = "dx-present-enter";

    /// <summary>CSS class applied while the child is animating out.</summary>
    [Parameter] public string LeaveClass { get; set; } = "dx-present-leave";

    /// <summary>How long to keep the child mounted after it is hidden, in milliseconds.</summary>
    [Parameter] public int ExitDurationMs { get; set; } = 200;

    protected override void OnParametersSet()
    {
        if (Visible && !present)
        {
            present = true;
            stateClass = EnterClass;
        }
        else if (!Visible && present)
        {
            _ = BeginLeaveAsync();
        }
    }

    private async Task BeginLeaveAsync()
    {
        stateClass = LeaveClass;
        StateHasChanged();

        await Task.Delay(ExitDurationMs);

        present = false;
        StateHasChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!present)
        {
            return;
        }

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", stateClass);
        builder.AddContent(2, ChildContent);
        builder.CloseElement();
    }
}
