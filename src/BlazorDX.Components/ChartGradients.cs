using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Shared SVG <c>&lt;defs&gt;</c> gradient rendering for charts that opt into a <c>Gradient</c>
/// parameter. Each gradient fades a mark's own color from full opacity at its base to translucent
/// at its tip — no hardcoded lighter-shade math, so it works with any color (hex, named, a CSS
/// variable) and stays theme-safe. One <c>&lt;linearGradient&gt;</c> is emitted per distinct color
/// actually in use.
/// </summary>
internal static class ChartGradients
{
    public static void RenderDefs(RenderTreeBuilder builder, int seq, string chartId, IEnumerable<string> colors)
    {
        string[] distinct = colors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinct.Length == 0)
        {
            return;
        }

        builder.OpenElement(seq, "defs");
        foreach (string color in distinct)
        {
            builder.OpenElement(seq + 1, "linearGradient");
            builder.SetKey(color);
            builder.AddAttribute(seq + 2, "id", Id(chartId, color));
            builder.AddAttribute(seq + 3, "x1", "0");
            builder.AddAttribute(seq + 4, "y1", "0");
            builder.AddAttribute(seq + 5, "x2", "0");
            builder.AddAttribute(seq + 6, "y2", "1");

            builder.OpenElement(seq + 7, "stop");
            builder.AddAttribute(seq + 8, "offset", "0%");
            builder.AddAttribute(seq + 9, "stop-color", color);
            builder.CloseElement();

            builder.OpenElement(seq + 10, "stop");
            builder.AddAttribute(seq + 11, "offset", "100%");
            builder.AddAttribute(seq + 12, "stop-color", color);
            builder.AddAttribute(seq + 13, "stop-opacity", "0.45");
            builder.CloseElement();

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    /// <summary>The <c>fill</c> value referencing the gradient built for <paramref name="color"/>.</summary>
    public static string Url(string chartId, string color) => $"url(#{Id(chartId, color)})";

    private static string Id(string chartId, string color) =>
        $"{chartId}-grad-{Math.Abs(color.GetHashCode(StringComparison.OrdinalIgnoreCase))}";
}
