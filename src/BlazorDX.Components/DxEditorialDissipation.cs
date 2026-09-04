using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A "data-as-art" moment for a <see cref="DxEditorialScrollyStage"/>: a grid of dots that
/// fade, shrink, and blur away once the parent stage gains its reveal class (see
/// dx-editorial.css) — a CSS-only study, no canvas/WebGL.
/// </summary>
public sealed class DxEditorialDissipation : ComponentBase
{
    [Parameter] public int DotCount { get; set; } = 24;

    [Parameter] public string? AriaLabel { get; set; }

    [Inject] private IServiceProvider Services { get; set; } = default!;

    private DxStrings<DxEditorialDissipation>? s;

    private DxStrings<DxEditorialDissipation> S => s ??= new(Services);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-editorial-dissipation");
        builder.AddAttribute(2, "role", "img");
        builder.AddAttribute(3, "aria-label", AriaLabel ?? S["DissipationLabel",
            "An illustration of ephemeral content fading away once its session ends."]);

        for (int i = 0; i < DotCount; i++)
        {
            builder.OpenElement(4, "span");
            builder.SetKey(i);
            builder.AddAttribute(5, "class", "dissolve");
            builder.AddAttribute(6, "style", $"--i:{i}");
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
