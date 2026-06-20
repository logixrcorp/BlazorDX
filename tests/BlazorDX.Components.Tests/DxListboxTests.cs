using BlazorDX.Components;
using BlazorDX.Primitives.Overlays;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + multi-select behavior for the inline listbox.</summary>
public sealed class DxListboxTests : TestContext
{
    private static readonly IReadOnlyList<ListOption<string>> Options =
    [
        new ListOption<string>("a", "Alpha"),
        new ListOption<string>("b", "Beta"),
        new ListOption<string>("c", "Gamma"),
    ];

    [Fact]
    public void Renders_options_with_listbox_semantics()
    {
        IRenderedComponent<DxListbox<string>> listbox = RenderComponent<DxListbox<string>>(parameters => parameters
            .Add(l => l.Items, Options)
            .Add(l => l.Multiple, true));

        Assert.Equal("true", listbox.Find("[role=listbox]").GetAttribute("aria-multiselectable"));
        Assert.Equal(3, listbox.FindAll("[role=option]").Count);
    }

    [Fact]
    public void Marks_bound_multi_selection_as_selected()
    {
        IRenderedComponent<DxListbox<string>> listbox = RenderComponent<DxListbox<string>>(parameters => parameters
            .Add(l => l.Items, Options)
            .Add(l => l.Multiple, true)
            .Add(l => l.Values, new[] { "a", "c" }));

        var options = listbox.FindAll("[role=option]");
        Assert.Equal("true", options[0].GetAttribute("aria-selected"));
        Assert.Equal("false", options[1].GetAttribute("aria-selected"));
        Assert.Equal("true", options[2].GetAttribute("aria-selected"));
    }

    [Fact]
    public void Clicking_options_accumulates_multi_selection()
    {
        IReadOnlyCollection<string> bound = [];
        IRenderedComponent<DxListbox<string>> listbox = RenderComponent<DxListbox<string>>(parameters => parameters
            .Add(l => l.Items, Options)
            .Add(l => l.Multiple, true)
            .Add(l => l.Values, bound)
            .Add(l => l.ValuesChanged, values => bound = values));

        listbox.FindAll("[role=option]")[0].Click();
        Assert.Contains("a", bound);

        // Re-render with the new bound value, then add a second.
        listbox.SetParametersAndRender(parameters => parameters.Add(l => l.Values, bound));
        listbox.FindAll("[role=option]")[2].Click();
        Assert.Contains("a", bound);
        Assert.Contains("c", bound);
    }
}
