using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// An "listen to this article" control for a <see cref="DxEditorialLayout"/> piece — wraps a
/// pre-recorded narration file in a native <c>&lt;audio controls&gt;</c> element rather than a
/// custom-styled player. BlazorDX ships no text-to-speech engine; this component assumes
/// <see cref="AudioSrc"/> is a real audio file the host application recorded or generated. A
/// custom play/pause UI would need JS interop this library's Editorial family deliberately
/// avoids, and native controls are already fully accessible and functional without it.
/// </summary>
public sealed class DxEditorialListen : ComponentBase
{
    [Parameter, EditorRequired] public string AudioSrc { get; set; } = "";

    [Parameter] public string Label { get; set; } = "Listen to this article";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-editorial-listen");

        builder.OpenElement(2, "p");
        builder.AddAttribute(3, "class", "dx-editorial-listen-label");
        builder.AddContent(4, Label);
        builder.CloseElement();

        builder.OpenElement(5, "audio");
        builder.AddAttribute(6, "class", "dx-editorial-listen-audio");
        builder.AddAttribute(7, "controls", true);
        builder.AddAttribute(8, "src", AudioSrc);
        builder.CloseElement();

        builder.CloseElement();
    }
}
