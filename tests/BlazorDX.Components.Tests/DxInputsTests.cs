using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Text, password, textarea, and generic-numeric input behavior.</summary>
public sealed class DxInputsTests : TestContext
{
    [Fact]
    public void TextBox_commits_value_on_change()
    {
        string? bound = null;
        IRenderedComponent<DxTextBox> box = RenderComponent<DxTextBox>(parameters => parameters
            .Add(t => t.Label, "Name")
            .Add(t => t.ValueChanged, v => bound = v));

        box.Find("input").Change("Ada");
        Assert.Equal("Ada", bound);
        Assert.Contains("Name", box.Markup);
    }

    [Fact]
    public void TextBox_immediate_binds_on_input()
    {
        string? bound = null;
        IRenderedComponent<DxTextBox> box = RenderComponent<DxTextBox>(parameters => parameters
            .Add(t => t.Immediate, true)
            .Add(t => t.ValueChanged, v => bound = v));

        box.Find("input").Input("A");
        Assert.Equal("A", bound);
    }

    [Fact]
    public void TextArea_renders_value_and_rows()
    {
        IRenderedComponent<DxTextArea> area = RenderComponent<DxTextArea>(parameters => parameters
            .Add(t => t.Value, "hello")
            .Add(t => t.Rows, 6));

        var el = area.Find("textarea");
        Assert.Equal("6", el.GetAttribute("rows"));
        Assert.Contains("hello", el.TextContent);
    }

    [Fact]
    public void Password_starts_masked_and_reveal_toggles_type()
    {
        IRenderedComponent<DxPassword> pwd = RenderComponent<DxPassword>(parameters => parameters
            .Add(p => p.Label, "Password"));

        Assert.Equal("password", pwd.Find("input").GetAttribute("type"));

        pwd.Find(".dx-input-affix").Click();
        Assert.Equal("text", pwd.Find("input").GetAttribute("type"));
        Assert.Equal("true", pwd.Find(".dx-input-affix").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Numeric_step_buttons_increment_and_decrement()
    {
        int? bound = 4;
        IRenderedComponent<DxNumeric<int>> num = RenderComponent<DxNumeric<int>>(parameters => parameters
            .Add(n => n.Value, bound)
            .Add(n => n.ValueChanged, v => bound = v));

        // Buttons render [−, +] around the input.
        var buttons = num.FindAll(".dx-num-step");
        buttons[1].Click();   // +
        Assert.Equal(5, bound);
        num.FindAll(".dx-num-step")[0].Click();   // −
        Assert.Equal(4, bound);
    }

    [Fact]
    public void Numeric_clamps_to_min_and_max()
    {
        int? bound = 1;
        IRenderedComponent<DxNumeric<int>> num = RenderComponent<DxNumeric<int>>(parameters => parameters
            .Add(n => n.Value, bound)
            .Add(n => n.Min, 1)
            .Add(n => n.Max, 3)
            .Add(n => n.ValueChanged, v => bound = v));

        // At min: the decrement button is disabled.
        Assert.True(num.FindAll(".dx-num-step")[0].HasAttribute("disabled"));

        // Typing past max clamps to max.
        num.Find(".dx-num-input").Change("99");
        Assert.Equal(3, bound);
    }

    [Fact]
    public void Numeric_parses_with_invariant_culture_and_format()
    {
        decimal? bound = null;
        IRenderedComponent<DxNumeric<decimal>> num = RenderComponent<DxNumeric<decimal>>(parameters => parameters
            .Add(n => n.Format, "F2")
            .Add(n => n.ValueChanged, v => bound = v));

        num.Find(".dx-num-input").Change("12.5");
        Assert.Equal(12.5m, bound);
        // Re-render with the value shows the formatted display.
        num.SetParametersAndRender(parameters => parameters.Add(n => n.Value, 12.5m));
        Assert.Equal("12.50", num.Find(".dx-num-input").GetAttribute("value"));
    }

    [Fact]
    public void Numeric_rejects_unparseable_input()
    {
        int? bound = 7;
        IRenderedComponent<DxNumeric<int>> num = RenderComponent<DxNumeric<int>>(parameters => parameters
            .Add(n => n.Value, bound)
            .Add(n => n.ValueChanged, v => bound = v));

        num.Find(".dx-num-input").Change("abc");
        Assert.Equal(7, bound);   // unchanged
    }
}
