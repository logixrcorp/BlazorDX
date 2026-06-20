using BlazorDX.Primitives.Motion;
using BlazorDX.Primitives.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled command palette (⌘K). Inherits all behavior from
/// <see cref="CommandPalettePrimitive"/> and renders a centered modal with a
/// filtering input and a command list. Styling is CSS-variable driven (see
/// dx-overlay.css).
/// </summary>
public sealed class DxCommandPalette : CommandPalettePrimitive
{
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<PresenceBoundary>(0);
        builder.AddComponentParameter(1, nameof(PresenceBoundary.Visible), Open);
        builder.AddComponentParameter(2, nameof(PresenceBoundary.ExitDurationMs), ExitDurationMs);
        builder.AddComponentParameter(3, nameof(PresenceBoundary.EnterClass), "dx-cmdk dx-dialog-enter");
        builder.AddComponentParameter(4, nameof(PresenceBoundary.LeaveClass), "dx-cmdk dx-dialog-leave");
        builder.AddComponentParameter(5, nameof(PresenceBoundary.ChildContent), (RenderFragment)RenderShell);
        builder.CloseComponent();
    }

    private void RenderShell(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-cmdk-backdrop");

        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "id", PanelId);
        builder.AddAttribute(4, "class", $"dx-cmdk-panel {Class}".TrimEnd());
        builder.AddAttribute(5, "role", "dialog");
        builder.AddAttribute(6, "aria-modal", "true");
        builder.AddAttribute(7, "aria-label", "Command palette");

        builder.OpenElement(8, "input");
        builder.AddAttribute(9, "class", "dx-cmdk-input");
        builder.AddAttribute(10, "type", "text");
        builder.AddAttribute(11, "role", "combobox");
        builder.AddAttribute(12, "aria-autocomplete", "list");
        builder.AddAttribute(13, "aria-controls", $"{PanelId}-list");
        builder.AddAttribute(14, "placeholder", Placeholder);
        builder.AddAttribute(15, "value", Filter);
        if (ActiveDescendantId.Length > 0)
        {
            builder.AddAttribute(16, "aria-activedescendant", ActiveDescendantId);
        }

        builder.AddAttribute(17, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, OnInput));
        builder.AddAttribute(18, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));
        builder.CloseElement();

        builder.OpenElement(19, "div");
        builder.AddAttribute(20, "id", $"{PanelId}-list");
        builder.AddAttribute(21, "class", "dx-cmdk-list");
        builder.AddAttribute(22, "role", "listbox");

        if (Filtered.Count == 0)
        {
            builder.OpenElement(23, "div");
            builder.AddAttribute(24, "class", "dx-cmdk-empty");
            builder.AddContent(25, "No commands");
            builder.CloseElement();
        }

        for (int index = 0; index < Filtered.Count; index++)
        {
            Command command = Filtered[index];
            int captured = index;

            builder.OpenElement(26, "div");
            builder.AddAttribute(27, "id", OptionId(index));
            builder.AddAttribute(28, "role", "option");
            builder.AddAttribute(29, "class", IsActive(index) ? "dx-cmdk-item dx-cmdk-active" : "dx-cmdk-item");
            builder.AddAttribute(30, "aria-selected", IsActive(index) ? "true" : "false");
            // mousedown (not click) so the command runs before the input blurs.
            builder.AddAttribute(31, "onmousedown", EventCallback.Factory.Create(this, () => RunAsync(captured)));
            builder.AddEventPreventDefaultAttribute(32, "onmousedown", true);

            if (command.Group is { Length: > 0 } group)
            {
                builder.OpenElement(33, "span");
                builder.AddAttribute(34, "class", "dx-cmdk-group");
                builder.AddContent(35, group);
                builder.CloseElement();
            }

            builder.AddContent(36, command.Title);
            builder.CloseElement();
        }

        builder.CloseElement();

        builder.CloseElement();
        builder.CloseElement();
    }
}
