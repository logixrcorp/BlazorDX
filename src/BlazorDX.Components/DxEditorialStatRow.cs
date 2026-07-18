using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A row of oversized numeric callouts for a <see cref="DxEditorialLayout"/> piece — the
/// data-journalism "big number" device. Use real facts, not decoration.
/// </summary>
public sealed class DxEditorialStatRow : ComponentBase
{
    public readonly record struct Stat(string Value, string Label, string? Detail = null);

    [Parameter, EditorRequired] public IReadOnlyList<Stat> Stats { get; set; } = [];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-editorial-stats");

        for (int i = 0; i < Stats.Count; i++)
        {
            Stat stat = Stats[i];
            builder.OpenElement(2, "div");
            builder.SetKey(stat);
            builder.AddAttribute(3, "class", "dx-editorial-stat");

            builder.OpenElement(4, "p");
            builder.AddAttribute(5, "class", "dx-editorial-stat-value");
            builder.AddContent(6, stat.Value);
            builder.CloseElement();

            builder.OpenElement(7, "p");
            builder.AddAttribute(8, "class", "dx-editorial-stat-label");
            builder.AddContent(9, stat.Label);
            builder.CloseElement();

            if (!string.IsNullOrEmpty(stat.Detail))
            {
                builder.OpenElement(10, "p");
                builder.AddAttribute(11, "class", "dx-editorial-stat-detail");
                builder.AddContent(12, stat.Detail);
                builder.CloseElement();
            }

            builder.CloseElement(); // .dx-editorial-stat
        }

        builder.CloseElement(); // .dx-editorial-stats
    }
}
