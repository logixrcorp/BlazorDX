using BlazorDX.Components;
using BlazorDX.Primitives.Overlays;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Checkbox, switch, and radio-group behavior.</summary>
public sealed class DxFormControlsTests : TestContext
{
    [Fact]
    public void Checkbox_toggles_value_on_change()
    {
        bool bound = false;
        IRenderedComponent<DxCheckbox> checkbox = RenderComponent<DxCheckbox>(parameters => parameters
            .Add(c => c.Value, bound)
            .Add(c => c.Label, "Accept")
            .Add(c => c.ValueChanged, v => bound = v));

        checkbox.Find("input[type=checkbox]").Change(true);
        Assert.True(bound);
    }

    [Fact]
    public void Switch_has_role_switch_and_reflects_value()
    {
        IRenderedComponent<DxSwitch> sw = RenderComponent<DxSwitch>(parameters => parameters
            .Add(s => s.Value, true));

        var input = sw.Find("input[role=switch]");
        Assert.Equal("true", input.GetAttribute("aria-checked"));
    }

    private static IReadOnlyList<ListOption<string>> Plans() =>
    [
        new ListOption<string>("free", "Free"),
        new ListOption<string>("pro", "Pro"),
        new ListOption<string>("team", "Team"),
    ];

    [Fact]
    public void RadioGroup_marks_the_selected_option()
    {
        IRenderedComponent<DxRadioGroup<string>> radios = RenderComponent<DxRadioGroup<string>>(parameters => parameters
            .Add(r => r.Items, Plans())
            .Add(r => r.Value, "pro"));

        var options = radios.FindAll("[role=radio]");
        Assert.Equal("true", options[1].GetAttribute("aria-checked"));
        Assert.Equal("0", options[1].GetAttribute("tabindex"));   // selected is the tab stop
        Assert.Equal("-1", options[0].GetAttribute("tabindex"));
    }

    [Fact]
    public void RadioGroup_click_selects()
    {
        string bound = "free";
        IRenderedComponent<DxRadioGroup<string>> radios = RenderComponent<DxRadioGroup<string>>(parameters => parameters
            .Add(r => r.Items, Plans())
            .Add(r => r.Value, bound)
            .Add(r => r.ValueChanged, v => bound = v));

        radios.FindAll("[role=radio]")[2].Click();
        Assert.Equal("team", bound);
    }

    [Fact]
    public void RadioGroup_arrow_down_moves_and_selects()
    {
        string bound = "free";
        IRenderedComponent<DxRadioGroup<string>> radios = RenderComponent<DxRadioGroup<string>>(parameters => parameters
            .Add(r => r.Items, Plans())
            .Add(r => r.Value, bound)
            .Add(r => r.ValueChanged, v => bound = v));

        radios.Find("[role=radiogroup]").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal("pro", bound); // free -> pro
    }
}
