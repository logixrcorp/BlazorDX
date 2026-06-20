using BlazorDX.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Renders only the items intersecting the scroll viewport (plus a small overscan),
/// keeping the DOM small for arbitrarily large lists. This is the grid's
/// virtualization windowing, generalized into a reusable component for any
/// fixed-height list (option lists, logs, timelines). Reuses the shared scroll/DOM
/// bridge; off-browser it renders the initial window.
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
public sealed class DxVirtualize<TItem> : ComponentBase, IAsyncDisposable
{
    private int firstVisible;
    private int visibleCount;
    private bool scrollSubscribed;

    [Parameter, EditorRequired] public IReadOnlyList<TItem> Items { get; set; } = [];

    [Parameter, EditorRequired] public RenderFragment<TItem> ChildContent { get; set; } = default!;

    /// <summary>Fixed height of each row in pixels (the windowing math depends on it).</summary>
    [Parameter] public int ItemHeight { get; set; } = 32;

    [Parameter] public int ViewportHeight { get; set; } = 400;

    [Parameter] public int Overscan { get; set; } = 8;

    [Parameter] public string? Class { get; set; }

    [Inject] private IGridDomInterop Dom { get; set; } = default!;

    private string ContainerId { get; } = $"dx-virt-{Guid.NewGuid():N}";

    private int LastVisible => Math.Min(Items.Count, firstVisible + visibleCount);

    protected override void OnParametersSet() =>
        visibleCount = (int)Math.Ceiling((double)ViewportHeight / ItemHeight) + (Overscan * 2);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        double topPadding = (double)firstVisible * ItemHeight;
        double bottomPadding = Math.Max(0, (double)(Items.Count - LastVisible) * ItemHeight);

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", ContainerId);
        builder.AddAttribute(2, "class", $"dx-virtualize {Class}".TrimEnd());
        builder.AddAttribute(3, "style", $"height:{ViewportHeight}px;overflow-y:auto;");

        builder.OpenElement(4, "div");
        builder.AddAttribute(5, "style", $"height:{topPadding}px;");
        builder.CloseElement();

        for (int i = firstVisible; i < LastVisible; i++)
        {
            builder.OpenElement(6, "div");
            builder.SetKey(i);
            builder.AddAttribute(7, "class", "dx-virtualize-item");
            builder.AddAttribute(8, "style", $"height:{ItemHeight}px;");
            builder.AddContent(9, ChildContent, Items[i]);
            builder.CloseElement();
        }

        builder.OpenElement(10, "div");
        builder.AddAttribute(11, "style", $"height:{bottomPadding}px;");
        builder.CloseElement();

        builder.CloseElement();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || scrollSubscribed || !OperatingSystem.IsBrowser())
        {
            return;
        }

        scrollSubscribed = true;
        await Dom.SubscribeScrollAsync(ContainerId, OnScroll);
        await UpdateWindowAsync();
    }

    private void OnScroll() => _ = UpdateWindowAsync();

    private async Task UpdateWindowAsync()
    {
        (double scrollTop, double clientHeight, _) = await Dom.MeasureViewportAsync(ContainerId);
        int viewport = clientHeight > 0 ? (int)clientHeight : ViewportHeight;

        int desiredFirst = Math.Max(0, (int)(scrollTop / ItemHeight) - Overscan);
        int desiredCount = (int)Math.Ceiling((double)viewport / ItemHeight) + (Overscan * 2);
        if (desiredFirst == firstVisible && desiredCount == visibleCount)
        {
            return;
        }

        firstVisible = desiredFirst;
        visibleCount = desiredCount;
        await InvokeAsync(StateHasChanged);
    }

    public ValueTask DisposeAsync() => Dom.DisposeAsync();
}
