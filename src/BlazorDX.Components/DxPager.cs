using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A standalone pagination control: first / previous / next / last plus a windowed
/// run of page numbers with ellipses. Page is 1-based and two-way bindable. Pure
/// presentation + arithmetic; pair it with any paged data source.
/// </summary>
public sealed class DxPager : ComponentBase
{
    [Parameter] public int TotalItems { get; set; }

    [Parameter] public int PageSize { get; set; } = 10;

    /// <summary>The current 1-based page. Two-way bindable.</summary>
    [Parameter] public int Page { get; set; } = 1;

    [Parameter] public EventCallback<int> PageChanged { get; set; }

    [Parameter] public string? Class { get; set; }

    /// <summary>The number of pages (at least one).</summary>
    public int PageCount => Math.Max(1, (int)Math.Ceiling((double)TotalItems / Math.Max(1, PageSize)));

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        int current = Math.Clamp(Page, 1, PageCount);

        builder.OpenElement(0, "nav");
        builder.AddAttribute(1, "class", $"dx-pager {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "navigation");
        builder.AddAttribute(3, "aria-label", "Pagination");

        Edge(builder, 4, "First page", "«", 1, current > 1);
        Edge(builder, 5, "Previous page", "‹", current - 1, current > 1);

        foreach (int? token in BuildTokens(current))
        {
            if (token is int page)
            {
                PageButton(builder, page, page == current);
            }
            else
            {
                builder.OpenElement(6, "span");
                builder.AddAttribute(7, "class", "dx-pager-ellipsis");
                builder.AddContent(8, "…");
                builder.CloseElement();
            }
        }

        Edge(builder, 9, "Next page", "›", current + 1, current < PageCount);
        Edge(builder, 10, "Last page", "»", PageCount, current < PageCount);

        builder.CloseElement();
    }

    // The pages to show: always 1 and last, plus the current page and its neighbours,
    // with nulls marking gaps (rendered as ellipses).
    private IReadOnlyList<int?> BuildTokens(int current)
    {
        SortedSet<int> pages = [1, PageCount];
        for (int page = current - 1; page <= current + 1; page++)
        {
            if (page >= 1 && page <= PageCount)
            {
                pages.Add(page);
            }
        }

        List<int?> tokens = new();
        int previous = 0;
        foreach (int page in pages)
        {
            if (previous != 0 && page - previous > 1)
            {
                tokens.Add(null); // gap
            }

            tokens.Add(page);
            previous = page;
        }

        return tokens;
    }

    private void PageButton(RenderTreeBuilder builder, int page, bool isCurrent)
    {
        builder.OpenElement(11, "button");
        builder.SetKey(page);
        builder.AddAttribute(12, "type", "button");
        builder.AddAttribute(13, "class", isCurrent ? "dx-pager-page dx-pager-current" : "dx-pager-page");
        if (isCurrent)
        {
            builder.AddAttribute(14, "aria-current", "page");
        }

        builder.AddAttribute(15, "onclick", EventCallback.Factory.Create(this, () => GoToAsync(page)));
        builder.AddContent(16, page);
        builder.CloseElement();
    }

    private void Edge(RenderTreeBuilder builder, int sequence, string label, string glyph, int target, bool enabled)
    {
        builder.OpenElement(sequence, "button");
        builder.AddAttribute(sequence + 20, "type", "button");
        builder.AddAttribute(sequence + 21, "class", "dx-pager-edge");
        builder.AddAttribute(sequence + 22, "aria-label", label);
        builder.AddAttribute(sequence + 23, "disabled", !enabled);
        if (enabled)
        {
            builder.AddAttribute(sequence + 24, "onclick", EventCallback.Factory.Create(this, () => GoToAsync(target)));
        }

        builder.AddContent(sequence + 25, glyph);
        builder.CloseElement();
    }

    private async Task GoToAsync(int page)
    {
        int clamped = Math.Clamp(page, 1, PageCount);
        if (clamped != Page && PageChanged.HasDelegate)
        {
            await PageChanged.InvokeAsync(clamped);
        }
    }
}
