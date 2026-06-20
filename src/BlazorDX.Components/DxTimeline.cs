using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>One event on a <see cref="DxTimeline"/>.</summary>
/// <param name="Title">Headline for the event.</param>
/// <param name="Time">Optional timestamp/label shown above the title.</param>
/// <param name="Description">Optional supporting text.</param>
/// <param name="Variant">Marker style hint: "default", "success", "warning", or "danger".</param>
public readonly record struct TimelineItem(
    string Title, string? Time = null, string? Description = null, string Variant = "default");

/// <summary>
/// A vertical timeline of events, each with a marker, an optional timestamp, and
/// connecting line. Renders a semantic ordered list. Styling is token-driven
/// (see dx-structure.css).
/// </summary>
public sealed class DxTimeline : ComponentBase
{
    [Parameter] public IReadOnlyList<TimelineItem> Items { get; set; } = [];

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "ol");
        builder.AddAttribute(1, "class", $"dx-timeline {Class}".TrimEnd());

        for (int index = 0; index < Items.Count; index++)
        {
            TimelineItem item = Items[index];

            builder.OpenElement(2, "li");
            builder.SetKey(index);
            builder.AddAttribute(3, "class", "dx-timeline-item");

            builder.OpenElement(4, "span");
            builder.AddAttribute(5, "class", $"dx-timeline-marker dx-timeline-{item.Variant}");
            builder.AddAttribute(6, "aria-hidden", "true");
            builder.CloseElement();

            builder.OpenElement(7, "div");
            builder.AddAttribute(8, "class", "dx-timeline-body");

            if (!string.IsNullOrEmpty(item.Time))
            {
                builder.OpenElement(9, "div");
                builder.AddAttribute(10, "class", "dx-timeline-time");
                builder.AddContent(11, item.Time);
                builder.CloseElement();
            }

            builder.OpenElement(12, "div");
            builder.AddAttribute(13, "class", "dx-timeline-title");
            builder.AddContent(14, item.Title);
            builder.CloseElement();

            if (!string.IsNullOrEmpty(item.Description))
            {
                builder.OpenElement(15, "div");
                builder.AddAttribute(16, "class", "dx-timeline-desc");
                builder.AddContent(17, item.Description);
                builder.CloseElement();
            }

            builder.CloseElement();
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
