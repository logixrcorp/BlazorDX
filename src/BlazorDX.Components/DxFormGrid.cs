using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Lays its child fields out in a responsive multi-column grid (collapsing to one
/// column on narrow viewports). A simple, modern way to organize a form's fields
/// without hand-written column math.
/// </summary>
public sealed class DxFormGrid : ComponentBase
{
    /// <summary>Maximum number of columns on a wide viewport.</summary>
    [Parameter] public int Columns { get; set; } = 2;

    /// <summary>The fields/content to lay out.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Extra CSS classes appended to the grid.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        int columns = Columns < 1 ? 1 : Columns;
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-form-grid {Class}".TrimEnd());
        builder.AddAttribute(2, "style", $"--dx-form-cols:{columns}");
        builder.AddContent(3, ChildContent);
        builder.CloseElement();
    }
}
