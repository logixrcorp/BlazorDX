using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Renders components under a German UI culture and asserts the German text — the counterpart to
/// <see cref="FrenchRenderingTests"/>, and for the same reason.
/// </summary>
/// <remarks>
/// <para>
/// <c>TranslationCompletenessTests</c> proves the <c>.de.resx</c> files line up with the invariant
/// ones. That is a check on the files: it would still pass if the German satellite assemblies were
/// never produced, or produced and never found, and every string here would quietly render in
/// English. Each language needs its own rendering test, because each is packaged separately and
/// can therefore fail on its own.
/// </para>
/// <para>
/// The cases cover the ways a resource can be reached, which fail independently: a plain component
/// resource, a shared marker type, a composite-format string, and a value coalesced from a nullable
/// parameter.
/// </para>
/// </remarks>
public sealed class GermanRenderingTests : TestContext
{
    public GermanRenderingTests()
    {
        Services.AddLocalization();
        Services.AddScoped<IChartZoomInterop, NullChartZoomInterop>();
    }

    [Fact]
    public void A_plain_component_resource_resolves_in_German()
    {
        using CultureScope _ = CultureScope.For("de-DE");

        IRenderedComponent<DxSpinner> spinner = RenderComponent<DxSpinner>();

        Assert.Equal("Wird geladen", spinner.Find("[aria-label]").GetAttribute("aria-label"));
    }

    [Fact]
    public void A_shared_marker_resource_resolves_in_German()
    {
        // DxChartResources is reached through a marker type rather than the component's own name.
        using CultureScope _ = CultureScope.For("de-DE");

        IRenderedComponent<DxSparkline> spark = RenderComponent<DxSparkline>(p => p
            .Add(c => c.Points, [new ChartPoint(X: 1, Y: 1), new ChartPoint(X: 2, Y: 2)]));

        Assert.Contains("Sparkline mit 2 Punkten", spark.Markup);
    }

    [Fact]
    public void A_composite_format_string_keeps_its_arguments_in_German()
    {
        // Drop the {0} and "Kreisdiagramm mit Segmenten" is still fluent German that says nothing.
        using CultureScope _ = CultureScope.For("de-DE");

        IRenderedComponent<DxPieChart> pie = RenderComponent<DxPieChart>(p => p
            .Add(c => c.Points, [
                new ChartPoint(X: 1, Y: 1, Category: "A"),
                new ChartPoint(X: 2, Y: 2, Category: "B"),
                new ChartPoint(X: 3, Y: 3, Category: "C"),
            ]));

        Assert.Contains("Kreisdiagramm mit 3 Segmenten", pie.Markup);
    }

    [Fact]
    public void A_coalesced_parameter_default_resolves_in_German()
    {
        using CultureScope _ = CultureScope.For("de-DE");

        IRenderedComponent<DxSkipLink> link = RenderComponent<DxSkipLink>();

        Assert.Contains("Zum Hauptinhalt springen", link.Markup);
    }

    [Fact]
    public void Key_names_use_the_German_keyboard_convention()
    {
        // Not a translation of the English word but the name on a German keyboard: Strg, not
        // "Kontrolle". The spoken form differs from the key cap on purpose, so both are asserted.
        using CultureScope _ = CultureScope.For("de-DE");

        IRenderedComponent<DxKbd> kbd = RenderComponent<DxKbd>(p => p.Add(c => c.Combo, "Ctrl+Shift"));

        Assert.Equal("Strg", kbd.FindAll("kbd.dx-kbd")[0].TextContent);
        Assert.Equal("Umschalt", kbd.FindAll("kbd.dx-kbd")[1].TextContent);
        Assert.Equal("Steuerung plus Umschalttaste", kbd.Find(".dx-kbd-combo").GetAttribute("aria-label"));
    }
}
