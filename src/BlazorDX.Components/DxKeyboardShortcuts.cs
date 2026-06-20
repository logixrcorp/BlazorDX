using BlazorDX.Primitives.Motion;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A "press ? for shortcuts" cheat-sheet overlay. Pass it the same
/// <see cref="Hotkey"/> list you register with <see cref="DxHotkeys"/> and it
/// renders a labelled table of each shortcut (description + a <see cref="DxKbd"/>
/// rendering of its combo). Controlled via <see cref="Open"/>/<see cref="OpenChanged"/>;
/// closes on backdrop click, the close button, or Escape. Styling is CSS-variable
/// driven (see dx-overlay.css).
/// </summary>
public sealed class DxKeyboardShortcuts : ComponentBase
{
    /// <summary>The shortcuts to list (typically the same list given to <see cref="DxHotkeys"/>).</summary>
    [Parameter] public IReadOnlyList<Hotkey> Bindings { get; set; } = [];

    /// <summary>Whether the overlay is shown. Controlled — pair with <see cref="OpenChanged"/>.</summary>
    [Parameter] public bool Open { get; set; }

    /// <summary>Raised when the overlay requests to open or close (backdrop, button, Escape).</summary>
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }

    /// <summary>Heading shown at the top of the panel.</summary>
    [Parameter] public string Title { get; set; } = "Keyboard shortcuts";

    /// <summary>Exit animation duration in milliseconds.</summary>
    [Parameter] public int ExitDurationMs { get; set; } = 150;

    /// <summary>Extra CSS classes appended to the panel.</summary>
    [Parameter] public string? Class { get; set; }

    private Task CloseAsync() => OpenChanged.InvokeAsync(false);

    private Task OnKeyDownAsync(KeyboardEventArgs e) =>
        e.Key == "Escape" ? CloseAsync() : Task.CompletedTask;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<PresenceBoundary>(0);
        builder.AddComponentParameter(1, nameof(PresenceBoundary.Visible), Open);
        builder.AddComponentParameter(2, nameof(PresenceBoundary.ExitDurationMs), ExitDurationMs);
        builder.AddComponentParameter(3, nameof(PresenceBoundary.EnterClass), "dx-keys dx-dialog-enter");
        builder.AddComponentParameter(4, nameof(PresenceBoundary.LeaveClass), "dx-keys dx-dialog-leave");
        builder.AddComponentParameter(5, nameof(PresenceBoundary.ChildContent), (RenderFragment)RenderShell);
        builder.CloseComponent();
    }

    private void RenderShell(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-keys-backdrop");
        builder.AddAttribute(2, "onclick", EventCallback.Factory.Create(this, CloseAsync));
        builder.AddAttribute(3, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));

        builder.OpenElement(4, "div");
        builder.AddAttribute(5, "class", $"dx-keys-panel {Class}".TrimEnd());
        builder.AddAttribute(6, "role", "dialog");
        builder.AddAttribute(7, "aria-modal", "true");
        builder.AddAttribute(8, "aria-label", Title);
        builder.AddEventStopPropagationAttribute(9, "onclick", true);

        // Header: title + close button.
        builder.OpenElement(10, "div");
        builder.AddAttribute(11, "class", "dx-keys-header");
        builder.OpenElement(12, "span");
        builder.AddContent(13, Title);
        builder.CloseElement();
        builder.OpenElement(14, "button");
        builder.AddAttribute(15, "type", "button");
        builder.AddAttribute(16, "class", "dx-keys-close");
        builder.AddAttribute(17, "aria-label", "Close");
        builder.AddAttribute(18, "autofocus", true);
        builder.AddAttribute(19, "onclick", EventCallback.Factory.Create(this, CloseAsync));
        builder.AddContent(20, "✕");
        builder.CloseElement();
        builder.CloseElement();

        // The shortcut rows (only those with an actual combo).
        builder.OpenElement(21, "dl");
        builder.AddAttribute(22, "class", "dx-keys-list");

        int seq = 23;
        bool any = false;
        foreach (Hotkey binding in Bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.Combo))
            {
                continue;
            }

            any = true;
            builder.OpenElement(seq++, "div");
            builder.AddAttribute(seq++, "class", "dx-keys-row");

            builder.OpenElement(seq++, "dt");
            builder.AddAttribute(seq++, "class", "dx-keys-label");
            builder.AddContent(seq++, string.IsNullOrWhiteSpace(binding.Description) ? binding.Combo : binding.Description);
            builder.CloseElement();

            builder.OpenElement(seq++, "dd");
            builder.AddAttribute(seq++, "class", "dx-keys-combo");
            builder.OpenComponent<DxKbd>(seq++);
            builder.AddComponentParameter(seq++, nameof(DxKbd.Combo), binding.Combo);
            builder.CloseComponent();
            builder.CloseElement();

            builder.CloseElement();
        }

        if (!any)
        {
            builder.OpenElement(seq++, "div");
            builder.AddAttribute(seq++, "class", "dx-keys-empty");
            builder.AddContent(seq++, "No shortcuts");
            builder.CloseElement();
        }

        builder.CloseElement();   // dl
        builder.CloseElement();   // panel
        builder.CloseElement();   // backdrop
    }
}
