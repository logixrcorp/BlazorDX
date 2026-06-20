using BlazorDX.Components;
using BlazorDX.Interop;
using BlazorDX.Primitives.Overlays;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + interaction for the styled single-select (no-op browser bridges).</summary>
public sealed class DxSelectTests : TestContext
{
    private static readonly IReadOnlyList<ListOption<string>> Options =
    [
        new ListOption<string>("ATX", "Austin"),
        new ListOption<string>("CAI", "Cairo", Disabled: true),
        new ListOption<string>("DEN", "Denver"),
    ];

    public DxSelectTests()
    {
        Services.AddScoped<IOverlayInterop, NullOverlayInterop>();
        Services.AddScoped<IAnchorInterop, NullAnchorInterop>();
    }

    [Fact]
    public void Shows_placeholder_until_a_value_is_set()
    {
        IRenderedComponent<DxSelect<string>> select = RenderComponent<DxSelect<string>>(parameters => parameters
            .Add(s => s.Items, Options)
            .Add(s => s.Placeholder, "Choose..."));

        string trigger = select.Find(".dx-select-trigger").OuterHtml;
        Assert.Contains("Choose...", trigger);
        Assert.Contains("dx-select-placeholder", trigger);
    }

    [Fact]
    public void Displays_the_selected_option_text()
    {
        IRenderedComponent<DxSelect<string>> select = RenderComponent<DxSelect<string>>(parameters => parameters
            .Add(s => s.Items, Options)
            .Add(s => s.Value, "DEN"));

        Assert.Contains("Denver", select.Find(".dx-select-trigger").OuterHtml);
    }

    [Fact]
    public void Clicking_an_option_raises_ValueChanged()
    {
        string? bound = null;
        IRenderedComponent<DxSelect<string>> select = RenderComponent<DxSelect<string>>(parameters => parameters
            .Add(s => s.Items, Options)
            .Add(s => s.ValueChanged, value => bound = value));

        select.Find(".dx-select-trigger").Click();        // open
        var options = select.FindAll("[role=option]");
        options[2].Click();                                // choose "Denver"

        Assert.Equal("DEN", bound);
    }

    [Fact]
    public void Selected_option_is_marked_aria_selected_when_open()
    {
        IRenderedComponent<DxSelect<string>> select = RenderComponent<DxSelect<string>>(parameters => parameters
            .Add(s => s.Items, Options)
            .Add(s => s.Value, "ATX"));

        select.Find(".dx-select-trigger").Click();         // open the listbox
        var options = select.FindAll("[role=option]");
        Assert.Equal("true", options[0].GetAttribute("aria-selected"));
        Assert.Equal("false", options[2].GetAttribute("aria-selected"));
    }
}
