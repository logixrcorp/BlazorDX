using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A word cloud: words sized by <see cref="WordCloudEntry.Weight"/>, spiral-packed
/// (<see cref="WordCloudLayout"/>) so they fill the available space without overlapping. A word
/// that can't find room within the chart's bounds is silently dropped by the layout — no
/// truncation error, it just doesn't appear (a caller with a `Words.Count` vs. rendered-count
/// mismatch can detect this). Pure SVG; styling via dx-chart.css.
/// </summary>
public sealed class DxWordCloud : ComponentBase
{
    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    [Parameter, EditorRequired] public IReadOnlyList<WordCloudEntry> Words { get; set; } = [];

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 360;

    [Parameter] public double MinFontSize { get; set; } = 12;

    [Parameter] public double MaxFontSize { get; set; } = 56;

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<WordCloudEntry> OnWordSelected { get; set; }

    private bool Interactive => OnWordSelected.HasDelegate;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
        List<WordCloudInput> inputs = Words.Select(w => new WordCloudInput(w.Text, w.Weight)).ToList();
        IReadOnlyList<WordCloudPlacement> placements = WordCloudLayout.Compute(inputs, Width, Height, MinFontSize, MaxFontSize);

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", "img");
        builder.AddAttribute(6, "aria-label", $"Word cloud with {placements.Count} of {Words.Count} words placed");

        for (int i = 0; i < placements.Count; i++)
        {
            WordCloudPlacement p = placements[i];
            WordCloudEntry word = Words[p.Index];
            string color = word.Color ?? Palette[p.Index % Palette.Length];

            builder.OpenElement(10, "text");
            builder.SetKey(p.Index);
            builder.AddAttribute(11, "class", "dx-wordcloud-word dx-chart-drawin");
            builder.AddAttribute(12, "x", F(p.X));
            builder.AddAttribute(13, "y", F(p.Y));
            builder.AddAttribute(14, "font-size", F(p.FontSize));
            builder.AddAttribute(15, "fill", color);
            builder.AddAttribute(16, "text-anchor", "middle");
            builder.AddAttribute(17, "dominant-baseline", "middle");
            builder.AddAttribute(18, "style", $"animation-delay:{i * 20}ms");

            if (interactive)
            {
                WordCloudEntry captured = word;
                builder.AddAttribute(19, "tabindex", "0");
                builder.AddAttribute(20, "onclick", EventCallback.Factory.Create(this, () => OnWordSelected.InvokeAsync(captured)));
            }

            builder.AddContent(21, word.Text);
            builder.OpenElement(22, "title");
            builder.AddContent(23, $"{word.Text}: {Num(word.Weight)}");
            builder.CloseElement();
            builder.CloseElement();
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
