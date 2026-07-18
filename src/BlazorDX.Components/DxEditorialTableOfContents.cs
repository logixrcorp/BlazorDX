using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A jump-link table of contents for a <see cref="DxEditorialLayout"/> piece — the web
/// descendant of a print magazine's contents page. Plain anchor links to caller-supplied
/// target IDs; no scrollspy/active-section tracking in this version (a deliberate scope cut,
/// not an oversight — most TOCs work fine without it).
/// </summary>
public sealed class DxEditorialTableOfContents : ComponentBase
{
    /// <summary>One entry: the link text and the id of the section it jumps to (without the leading '#').</summary>
    public readonly record struct TocEntry(string Label, string TargetId);

    [Parameter, EditorRequired] public IReadOnlyList<TocEntry> Entries { get; set; } = [];

    [Parameter] public string Heading { get; set; } = "Contents";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "nav");
        builder.AddAttribute(1, "class", "dx-editorial-toc");
        builder.AddAttribute(2, "aria-label", Heading);

        builder.OpenElement(3, "p");
        builder.AddAttribute(4, "class", "dx-editorial-toc-heading");
        builder.AddContent(5, Heading);
        builder.CloseElement();

        builder.OpenElement(6, "ol");
        builder.AddAttribute(7, "class", "dx-editorial-toc-list");

        for (int i = 0; i < Entries.Count; i++)
        {
            TocEntry entry = Entries[i];
            builder.OpenElement(8, "li");
            builder.SetKey(entry);

            builder.OpenElement(9, "a");
            builder.AddAttribute(10, "href", $"#{entry.TargetId}");
            builder.AddContent(11, entry.Label);
            builder.CloseElement();

            builder.CloseElement(); // li
        }

        builder.CloseElement(); // ol
        builder.CloseElement(); // nav
    }
}
