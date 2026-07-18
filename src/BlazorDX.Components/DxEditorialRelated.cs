using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A "more like this" card row for the end of a <see cref="DxEditorialLayout"/> piece. Plain
/// non-generic entries (mirroring <see cref="DxEditorialFooter.FooterCard"/>) rather than a
/// generic type parameter — map your own content model to <see cref="RelatedEntry"/> with a
/// simple <c>Select</c> before passing it in.
/// </summary>
public sealed class DxEditorialRelated : ComponentBase
{
    public readonly record struct RelatedEntry(string Title, string Summary, string Route, string? Category = null);

    [Parameter, EditorRequired] public IReadOnlyList<RelatedEntry> Entries { get; set; } = [];

    [Parameter] public string Heading { get; set; } = "More from Insights";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Entries.Count == 0)
        {
            return;
        }

        builder.OpenElement(0, "section");
        builder.AddAttribute(1, "class", "dx-editorial-related");
        builder.AddAttribute(2, "aria-label", Heading);

        builder.OpenElement(3, "p");
        builder.AddAttribute(4, "class", "dx-editorial-related-heading");
        builder.AddContent(5, Heading);
        builder.CloseElement();

        builder.OpenElement(6, "div");
        builder.AddAttribute(7, "class", "dx-editorial-related-grid");

        for (int i = 0; i < Entries.Count; i++)
        {
            RelatedEntry entry = Entries[i];
            builder.OpenElement(8, "a");
            builder.SetKey(entry);
            builder.AddAttribute(9, "class", "dx-editorial-related-card");
            builder.AddAttribute(10, "href", entry.Route);

            if (!string.IsNullOrEmpty(entry.Category))
            {
                builder.OpenElement(11, "p");
                builder.AddAttribute(12, "class", "dx-editorial-related-card-category");
                builder.AddContent(13, entry.Category);
                builder.CloseElement();
            }

            builder.OpenElement(14, "p");
            builder.AddAttribute(15, "class", "dx-editorial-related-card-title");
            builder.AddContent(16, entry.Title);
            builder.CloseElement();

            builder.OpenElement(17, "p");
            builder.AddAttribute(18, "class", "dx-editorial-related-card-summary");
            builder.AddContent(19, entry.Summary);
            builder.CloseElement();

            builder.CloseElement(); // a
        }

        builder.CloseElement(); // .dx-editorial-related-grid
        builder.CloseElement(); // section
    }
}
