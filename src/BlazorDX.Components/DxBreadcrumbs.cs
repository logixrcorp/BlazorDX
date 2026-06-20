using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>One entry in a <see cref="DxBreadcrumbs"/> trail.</summary>
/// <param name="Text">Visible label.</param>
/// <param name="Href">Optional link target; the last item is rendered as the current page.</param>
public readonly record struct BreadcrumbItem(string Text, string? Href = null);

/// <summary>
/// A breadcrumb navigation trail. Renders a semantic <c>&lt;nav&gt;</c> /
/// <c>&lt;ol&gt;</c>; the final item is marked <c>aria-current="page"</c> and is
/// never a link. Styling is token-driven (see dx-structure.css).
/// </summary>
public sealed class DxBreadcrumbs : ComponentBase
{
    [Parameter] public IReadOnlyList<BreadcrumbItem> Items { get; set; } = [];

    /// <summary>Separator glyph between items.</summary>
    [Parameter] public string Separator { get; set; } = "/";

    [Parameter] public string AriaLabel { get; set; } = "Breadcrumb";

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "nav");
        builder.AddAttribute(1, "class", $"dx-breadcrumbs {Class}".TrimEnd());
        builder.AddAttribute(2, "aria-label", AriaLabel);

        builder.OpenElement(3, "ol");
        builder.AddAttribute(4, "class", "dx-breadcrumbs-list");

        for (int index = 0; index < Items.Count; index++)
        {
            BreadcrumbItem item = Items[index];
            bool isLast = index == Items.Count - 1;

            builder.OpenElement(5, "li");
            builder.SetKey(index);
            builder.AddAttribute(6, "class", "dx-breadcrumb-item");

            if (isLast || string.IsNullOrEmpty(item.Href))
            {
                builder.OpenElement(7, "span");
                builder.AddAttribute(8, "class", "dx-breadcrumb-current");
                if (isLast)
                {
                    builder.AddAttribute(9, "aria-current", "page");
                }

                builder.AddContent(10, item.Text);
                builder.CloseElement();
            }
            else
            {
                builder.OpenElement(11, "a");
                builder.AddAttribute(12, "class", "dx-breadcrumb-link");
                builder.AddAttribute(13, "href", item.Href);
                builder.AddContent(14, item.Text);
                builder.CloseElement();
            }

            if (!isLast)
            {
                builder.OpenElement(15, "span");
                builder.AddAttribute(16, "class", "dx-breadcrumb-sep");
                builder.AddAttribute(17, "aria-hidden", "true");
                builder.AddContent(18, Separator);
                builder.CloseElement();
            }

            builder.CloseElement();
        }

        builder.CloseElement();
        builder.CloseElement();
    }
}
