using BlazorDX.Components;
using BlazorDX.Interop;
using BlazorDX.Primitives.Overlays;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + typeahead filtering for the combo box (no-op browser bridges).</summary>
public sealed class DxComboBoxTests : TestContext
{
    private static readonly IReadOnlyList<ListOption<string>> Options =
    [
        new ListOption<string>("ATX", "Austin"),
        new ListOption<string>("BER", "Berlin"),
        new ListOption<string>("BRN", "Brno"),
        new ListOption<string>("DEN", "Denver"),
    ];

    public DxComboBoxTests()
    {
        Services.AddScoped<IOverlayInterop, NullOverlayInterop>();
        Services.AddScoped<IAnchorInterop, NullAnchorInterop>();
    }

    [Fact]
    public void Renders_a_combobox_input()
    {
        IRenderedComponent<DxComboBox<string>> combo = RenderComponent<DxComboBox<string>>(parameters => parameters
            .Add(c => c.Items, Options)
            .Add(c => c.Placeholder, "Type a city..."));

        var input = combo.Find("input[role=combobox]");
        Assert.Equal("Type a city...", input.GetAttribute("placeholder"));
    }

    [Fact]
    public void Typing_filters_options_case_insensitively()
    {
        IRenderedComponent<DxComboBox<string>> combo = RenderComponent<DxComboBox<string>>(parameters => parameters
            .Add(c => c.Items, Options));

        combo.Find("input[role=combobox]").Input("b");

        var options = combo.FindAll("[role=option]");
        Assert.Equal(2, options.Count);                    // Berlin, Brno (both contain "b")
        Assert.Contains("Berlin", combo.Markup);
        Assert.Contains("Brno", combo.Markup);
        Assert.DoesNotContain("Austin", combo.Markup);
    }

    [Fact]
    public void Selecting_a_filtered_option_raises_ValueChanged()
    {
        string? bound = null;
        IRenderedComponent<DxComboBox<string>> combo = RenderComponent<DxComboBox<string>>(parameters => parameters
            .Add(c => c.Items, Options)
            .Add(c => c.ValueChanged, value => bound = value));

        combo.Find("input[role=combobox]").Input("den");
        combo.FindAll("[role=option]")[0].MouseDown();

        Assert.Equal("DEN", bound);
    }

    [Fact]
    public void Shows_no_matches_message_when_filter_excludes_all()
    {
        IRenderedComponent<DxComboBox<string>> combo = RenderComponent<DxComboBox<string>>(parameters => parameters
            .Add(c => c.Items, Options));

        combo.Find("input[role=combobox]").Input("zzz");

        Assert.Contains("No matches", combo.Markup);
        Assert.Empty(combo.FindAll("[role=option]"));
    }
}
