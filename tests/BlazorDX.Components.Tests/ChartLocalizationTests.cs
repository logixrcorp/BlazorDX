using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The chart family localizes against one shared resource type, <see cref="DxChartResources"/>,
/// rather than one <c>.resx</c> per chart. These tests cover what that sharing can get wrong.
/// </summary>
/// <remarks>
/// Every chart's accessible summary is its <i>only</i> user-facing string, and for a chart it is
/// the whole accessibility story: the SVG is opaque to a screen reader, so this label is what a
/// non-sighted user gets instead of the picture. Losing it to a broken lookup would be a silent
/// accessibility regression that no axe rule catches — axe checks that an accessible name exists,
/// not that it says anything true.
/// </remarks>
public sealed class ChartLocalizationTests : TestContext
{
    public ChartLocalizationTests()
    {
        // The zoomable charts inject this; the null implementation reports no measurement.
        Services.AddScoped<IChartZoomInterop, NullChartZoomInterop>();
    }

    private void UseSentinel() =>
        Services.AddSingleton<IStringLocalizer<DxChartResources>>(new FakeStringLocalizer<DxChartResources>());

    [Fact]
    public void Each_chart_resolves_its_own_key_from_the_shared_resource()
    {
        // The failure mode of a shared resource file: two charts pointing at one key, so a
        // translator's wording for one silently becomes the wording for the other. Rendering
        // three different chart kinds against the sentinel shows each reaches a distinct key.
        UseSentinel();

        IRenderedComponent<DxPieChart> pie = RenderComponent<DxPieChart>(p => p
            .Add(c => c.Points, Points(2)));
        IRenderedComponent<DxSparkline> spark = RenderComponent<DxSparkline>(p => p
            .Add(c => c.Points, Points(3)));
        IRenderedComponent<DxWaterfallChart> waterfall = RenderComponent<DxWaterfallChart>(p => p
            .Add(c => c.Points, Points(1)));

        Assert.Contains("§§PIECHARTLABEL§§", pie.Markup);
        Assert.Contains("§§SPARKLINELABEL§§", spark.Markup);
        Assert.Contains("§§WATERFALLCHARTLABEL§§", waterfall.Markup);
    }

    [Fact]
    public void Chart_labels_fall_back_to_the_invariant_resource_with_their_counts()
    {
        // Proves DxChartResources.resx round-trips through the real factory *and* that the
        // composite-format arguments still land — a resource whose "{0}" was dropped in
        // translation would render a label with no numbers, which reads as correct English.
        Services.AddLocalization();

        IRenderedComponent<DxPieChart> pie = RenderComponent<DxPieChart>(p => p
            .Add(c => c.Points, Points(3)));

        Assert.Contains("Pie chart with 3 slices", pie.Markup);
    }

    [Fact]
    public void Charts_render_their_labels_in_English_with_no_localizer_registered()
    {
        // No AddLocalization() anywhere in this test: the point of ADR 0021. Before it, injecting
        // a localizer into fifteen charts would have made AddLocalization() mandatory for anyone
        // rendering a chart at all.
        IRenderedComponent<DxSparkline> spark = RenderComponent<DxSparkline>(p => p
            .Add(c => c.Points, Points(4)));

        Assert.Contains("Sparkline of 4 points", spark.Markup);
    }

    [Fact]
    public void The_stacked_and_grouped_bar_labels_are_separate_whole_sentences()
    {
        // Not "Stacked"/"Grouped" spliced into a shared frame: an adjective cannot be composed
        // with a noun phrase reliably across languages, so each variant is its own sentence.
        UseSentinel();

        IRenderedComponent<DxStackedBarChart> stacked = RenderComponent<DxStackedBarChart>(p => p
            .Add(c => c.Categories, new[] { "Q1" })
            .Add(c => c.Points, Stacked())
            .Add(c => c.Stacked, true));

        IRenderedComponent<DxStackedBarChart> grouped = RenderComponent<DxStackedBarChart>(p => p
            .Add(c => c.Categories, new[] { "Q1" })
            .Add(c => c.Points, Stacked())
            .Add(c => c.Stacked, false));

        Assert.Contains("§§STACKEDBARCHARTLABEL§§", stacked.Markup);
        Assert.Contains("§§GROUPEDBARCHARTLABEL§§", grouped.Markup);
    }

    private static IReadOnlyList<ChartPoint> Points(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new ChartPoint(X: i, Y: i, Category: $"P{i}"))];

    private static IReadOnlyList<ChartPoint> Stacked() =>
    [
        new(Category: "Q1", Y: 10, Series: "A"),
        new(Category: "Q1", Y: 5, Series: "B"),
    ];
}
