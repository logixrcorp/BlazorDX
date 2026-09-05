using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Renders components under a French UI culture and asserts the French text.
/// </summary>
/// <remarks>
/// <para>
/// <c>TranslationCompletenessTests</c> proves the <c>.fr.resx</c> files line up with the invariant
/// ones. That is a check on the files, not on the build: it would still pass if the satellite
/// assemblies were never produced, or produced and never found, in which case every one of these
/// strings would quietly render in English. This is the test that fails in that case.
/// </para>
/// <para>
/// The components below are chosen to cover the ways a resource can be reached, because they fail
/// independently: a plain component resource, a shared marker type (<c>DxChartResources</c>), a
/// composite-format string whose arguments have to survive translation, and a value coalesced from
/// a nullable parameter.
/// </para>
/// </remarks>
public sealed class FrenchRenderingTests : TestContext
{
    public FrenchRenderingTests()
    {
        Services.AddLocalization();
        Services.AddScoped<IChartZoomInterop, NullChartZoomInterop>();
    }

    [Fact]
    public void A_plain_component_resource_resolves_in_French()
    {
        using CultureScope _ = CultureScope.For("fr-FR");

        IRenderedComponent<DxSpinner> spinner = RenderComponent<DxSpinner>();

        Assert.Equal("Chargement", spinner.Find("[aria-label]").GetAttribute("aria-label"));
    }

    [Fact]
    public void A_shared_marker_resource_resolves_in_French()
    {
        // DxChartResources is reached through a marker type rather than the component's own name,
        // so it is packaged and resolved by a different path than DxSpinner's.
        using CultureScope _ = CultureScope.For("fr-FR");

        IRenderedComponent<DxSparkline> spark = RenderComponent<DxSparkline>(p => p
            .Add(c => c.Points, [new ChartPoint(X: 1, Y: 1), new ChartPoint(X: 2, Y: 2)]));

        Assert.Contains("Graphique sparkline de 2 points", spark.Markup);
    }

    [Fact]
    public void A_composite_format_string_keeps_its_arguments_in_French()
    {
        // The failure this catches is a translation that reads correctly and says nothing: drop
        // the {0} and "Graphique en secteurs avec parts" is still fluent French.
        using CultureScope _ = CultureScope.For("fr-FR");

        IRenderedComponent<DxPieChart> pie = RenderComponent<DxPieChart>(p => p
            .Add(c => c.Points, [
                new ChartPoint(X: 1, Y: 1, Category: "A"),
                new ChartPoint(X: 2, Y: 2, Category: "B"),
                new ChartPoint(X: 3, Y: 3, Category: "C"),
            ]));

        Assert.Contains("Graphique en secteurs avec 3 parts", pie.Markup);
    }

    [Fact]
    public void A_coalesced_parameter_default_resolves_in_French()
    {
        // The nullable-[Parameter] pattern: nothing supplied, so the localized default is used.
        using CultureScope _ = CultureScope.For("fr-FR");

        IRenderedComponent<DxSkipLink> link = RenderComponent<DxSkipLink>();

        Assert.Contains("Aller au contenu principal", link.Markup);
    }

    [Fact]
    public void An_unsupported_culture_falls_back_to_English_rather_than_a_key()
    {
        // The chain that matters when a language is not shipped: de-DE has no satellite assembly,
        // so resolution walks to the invariant resource. Rendering the key ("Loading") would look
        // almost right here, which is why the assertion is on the English word and not on absence.
        using CultureScope _ = CultureScope.For("de-DE");

        IRenderedComponent<DxSpinner> spinner = RenderComponent<DxSpinner>();

        Assert.Equal("Loading", spinner.Find("[aria-label]").GetAttribute("aria-label"));
    }
}
