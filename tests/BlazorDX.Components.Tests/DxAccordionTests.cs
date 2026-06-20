using BlazorDX.Components;
using BlazorDX.Primitives.Navigation;
using Bunit;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + expand/collapse behavior for the styled accordion.</summary>
public sealed class DxAccordionTests : TestContext
{
    private static IReadOnlyList<AccordionItem> BuildItems() =>
    [
        new AccordionItem("Alpha", b => b.AddContent(0, "Alpha body")),
        new AccordionItem("Beta", b => b.AddContent(0, "Beta body")),
    ];

    [Fact]
    public void Initially_expanded_section_shows_its_region()
    {
        IRenderedComponent<DxAccordion> accordion = RenderComponent<DxAccordion>(parameters => parameters
            .Add(a => a.Items, BuildItems())
            .Add(a => a.InitiallyExpanded, 0));

        Assert.Contains("Alpha body", accordion.Markup);
        Assert.DoesNotContain("Beta body", accordion.Markup);
        Assert.Equal("true", accordion.FindAll(".dx-accordion-header")[0].GetAttribute("aria-expanded"));
        Assert.Equal("false", accordion.FindAll(".dx-accordion-header")[1].GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Single_mode_expanding_one_collapses_the_other()
    {
        IRenderedComponent<DxAccordion> accordion = RenderComponent<DxAccordion>(parameters => parameters
            .Add(a => a.Items, BuildItems())
            .Add(a => a.Multiple, false)
            .Add(a => a.InitiallyExpanded, 0));

        accordion.FindAll(".dx-accordion-header")[1].Click(); // open Beta

        Assert.Contains("Beta body", accordion.Markup);
        Assert.DoesNotContain("Alpha body", accordion.Markup); // Alpha collapsed
    }

    [Fact]
    public void Multiple_mode_keeps_sections_independent()
    {
        IRenderedComponent<DxAccordion> accordion = RenderComponent<DxAccordion>(parameters => parameters
            .Add(a => a.Items, BuildItems())
            .Add(a => a.Multiple, true)
            .Add(a => a.InitiallyExpanded, 0));

        accordion.FindAll(".dx-accordion-header")[1].Click(); // also open Beta

        Assert.Contains("Alpha body", accordion.Markup);
        Assert.Contains("Beta body", accordion.Markup);
    }
}
