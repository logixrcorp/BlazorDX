using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// The footnote list for a <see cref="DxEditorialLayout"/> piece, typically placed at the end.
/// Each entry gets a back-link to its matching <see cref="DxEditorialFootnoteRef"/> marker.
/// </summary>
public sealed class DxEditorialFootnotes : ComponentBase
{
    public readonly record struct FootnoteEntry(int Number, string Text);

    [Parameter, EditorRequired] public IReadOnlyList<FootnoteEntry> Entries { get; set; } = [];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Entries.Count == 0)
        {
            return;
        }

        builder.OpenElement(0, "section");
        builder.AddAttribute(1, "class", "dx-editorial-footnotes");
        builder.AddAttribute(2, "aria-label", "Footnotes");

        builder.OpenElement(3, "ol");

        for (int i = 0; i < Entries.Count; i++)
        {
            FootnoteEntry entry = Entries[i];
            builder.OpenElement(4, "li");
            builder.SetKey(entry);
            builder.AddAttribute(5, "id", $"fn-{entry.Number}");

            builder.AddContent(6, entry.Text);
            builder.AddContent(7, " ");

            builder.OpenElement(8, "a");
            builder.AddAttribute(9, "href", $"#fnref-{entry.Number}");
            builder.AddAttribute(10, "class", "dx-editorial-footnote-back");
            builder.AddAttribute(11, "aria-label", $"Back to reference {entry.Number}");
            builder.AddContent(12, "↩");
            builder.CloseElement();

            builder.CloseElement(); // li
        }

        builder.CloseElement(); // ol
        builder.CloseElement(); // section
    }
}
