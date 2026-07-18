using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A row of topic/category pills for a <see cref="DxEditorialLayout"/> piece — the web
/// counterpart to a print magazine's "department" labeling. Each tag is a real link (not a
/// <see cref="DxChip"/>, which has no href) since tags typically navigate to a filtered or
/// archive view.
/// </summary>
public sealed class DxEditorialTagList : ComponentBase
{
    public readonly record struct Tag(string Label, string Href);

    [Parameter, EditorRequired] public IReadOnlyList<Tag> Tags { get; set; } = [];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "ul");
        builder.AddAttribute(1, "class", "dx-editorial-tags");
        builder.AddAttribute(2, "aria-label", "Topics");

        for (int i = 0; i < Tags.Count; i++)
        {
            Tag tag = Tags[i];
            builder.OpenElement(3, "li");
            builder.SetKey(tag);

            builder.OpenElement(4, "a");
            builder.AddAttribute(5, "class", "dx-editorial-tag");
            builder.AddAttribute(6, "href", tag.Href);
            builder.AddContent(7, tag.Label);
            builder.CloseElement();

            builder.CloseElement(); // li
        }

        builder.CloseElement(); // ul
    }
}
