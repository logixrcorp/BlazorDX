using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>ColorPicker, MaskedTextBox (mask logic), and the dual-thumb RangeSlider.</summary>
public sealed class InputControlsTests : TestContext
{
    [Theory]
    [InlineData("(000) 000-0000", "1234567890", "(123) 456-7890")]
    [InlineData("(000) 000-0000", "123", "(123")]                 // trailing literals dropped
    [InlineData("000", "1a2b3", "123")]                            // non-digits skipped
    [InlineData("AA-00", "ab12", "ab-12")]                         // letters + literal
    [InlineData("(000) 000-0000", "(123) 456-7890", "(123) 456-7890")]  // reformat is idempotent
    public void Mask_formats_input(string mask, string raw, string expected)
    {
        Assert.Equal(expected, DxMaskedTextBox.ApplyMask(mask, raw));
    }

    [Fact]
    public void MaskedTextBox_remasks_on_input()
    {
        string? captured = null;
        IRenderedComponent<DxMaskedTextBox> box = RenderComponent<DxMaskedTextBox>(p => p
            .Add(m => m.Mask, "(000) 000-0000")
            .Add(m => m.ValueChanged, EventCallback.Factory.Create<string>(this, v => captured = v)));

        box.Find("input.dx-input").Input("1234567890");
        Assert.Equal("(123) 456-7890", captured);
    }

    [Fact]
    public void ColorPicker_renders_value_and_presets_and_raises_change()
    {
        string? captured = null;
        IRenderedComponent<DxColorPicker> picker = RenderComponent<DxColorPicker>(p => p
            .Add(c => c.Value, "#ff0000")
            .Add(c => c.Presets, new[] { "#00ff00", "#0000ff" })
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => captured = v)));

        Assert.Equal("#ff0000", picker.Find("input.dx-colorpicker-input").GetAttribute("value"));
        Assert.Equal("#ff0000", picker.Find(".dx-colorpicker-value").TextContent);
        Assert.Equal(2, picker.FindAll(".dx-colorpicker-swatch").Count);

        picker.FindAll(".dx-colorpicker-swatch")[0].Click();   // first preset = #00ff00
        Assert.Equal("#00ff00", captured);
    }

    [Fact]
    public void RangeSlider_renders_both_thumbs_and_the_fill_position()
    {
        IRenderedComponent<DxRangeSlider> range = RenderComponent<DxRangeSlider>(p => p
            .Add(r => r.Low, 20.0)
            .Add(r => r.High, 80.0)
            .Add(r => r.Min, 0.0)
            .Add(r => r.Max, 100.0));

        Assert.Equal("20", range.Find(".dx-range-low").GetAttribute("value"));
        Assert.Equal("80", range.Find(".dx-range-high").GetAttribute("value"));

        string fill = range.Find(".dx-range-fill").GetAttribute("style")!;
        Assert.Contains("left:20%", fill);
        Assert.Contains("width:60%", fill);
    }

    [Fact]
    public void RangeSlider_clamps_thumbs_so_they_do_not_cross()
    {
        double? low = null;
        IRenderedComponent<DxRangeSlider> range = RenderComponent<DxRangeSlider>(p => p
            .Add(r => r.Low, 20.0)
            .Add(r => r.High, 80.0)
            .Add(r => r.Min, 0.0)
            .Add(r => r.Max, 100.0)
            .Add(r => r.LowChanged, EventCallback.Factory.Create<double>(this, v => low = v)));

        // Drag the low thumb past the high thumb → clamped to High (80).
        range.Find(".dx-range-low").Input("90");
        Assert.Equal(80, low);
    }
}
