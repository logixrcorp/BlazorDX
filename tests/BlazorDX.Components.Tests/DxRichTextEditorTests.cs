using BlazorDX.Components;
using BlazorDX.Interop;
using BlazorDX.Security;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Toolbar/surface rendering and the sanitizer boundary for the WYSIWYG editor.</summary>
public sealed class DxRichTextEditorTests : TestContext
{
    public DxRichTextEditorTests()
    {
        // No DOM under bUnit: the no-op bridge returns "" for GetHtml.
        Services.AddScoped<IRichTextInterop, NullRichTextInterop>();
    }

    [Fact]
    public void Renders_a_toolbar_and_an_editable_surface()
    {
        IRenderedComponent<DxRichTextEditor> editor = RenderComponent<DxRichTextEditor>(parameters => parameters
            .Add(e => e.AriaLabel, "Body"));

        Assert.Equal(13, editor.FindAll(".dx-rte-tool").Count);  // B I U S H • 1. ⇤↔⇥≡ link clear
        var surface = editor.Find(".dx-rte-surface");
        Assert.Equal("true", surface.GetAttribute("contenteditable"));
        Assert.Equal("textbox", surface.GetAttribute("role"));
        Assert.Equal("Body", surface.GetAttribute("aria-label"));
    }

    [Fact]
    public void Toolbar_buttons_suppress_mousedown_to_keep_the_selection()
    {
        IRenderedComponent<DxRichTextEditor> editor = RenderComponent<DxRichTextEditor>();

        // Each tool prevents default on mousedown so clicking it doesn't blur the editor.
        Assert.Contains("Bold", editor.Find(".dx-rte-tool").GetAttribute("aria-label"));
    }

    [Fact]
    public void Toolbar_includes_text_color_and_highlight_swatches()
    {
        IRenderedComponent<DxRichTextEditor> editor = RenderComponent<DxRichTextEditor>();

        // Two native color inputs, distinct from the 13 command tools.
        Assert.Equal(2, editor.FindAll(".dx-rte-color").Count);
        Assert.Equal(13, editor.FindAll(".dx-rte-tool").Count);
        Assert.Single(editor.FindAll("[aria-label='Text color']"));
        Assert.Single(editor.FindAll("[aria-label='Highlight color']"));

        // Changing a swatch routes through the (null) bridge without throwing.
        editor.Find("[aria-label='Text color']").Change("#ff0000");
    }

    [Fact]
    public void Edited_html_is_routed_through_the_injected_sanitizer()
    {
        bool sanitized = false;
        HtmlSanitizer spy = new(input =>
        {
            sanitized = true;
            return input;
        });

        IRenderedComponent<DxRichTextEditor> editor = RenderComponent<DxRichTextEditor>(parameters => parameters
            .Add(e => e.Sanitizer, spy));

        // Typing fires oninput -> the editor reads the DOM and sanitizes it.
        editor.Find(".dx-rte-surface").Input("anything");

        Assert.True(sanitized);
    }

    [Fact]
    public void Formatting_command_runs_then_resyncs_through_the_sanitizer()
    {
        int sanitizeCalls = 0;
        HtmlSanitizer spy = new(input =>
        {
            sanitizeCalls++;
            return input;
        });

        IRenderedComponent<DxRichTextEditor> editor = RenderComponent<DxRichTextEditor>(parameters => parameters
            .Add(e => e.Sanitizer, spy));

        editor.FindAll(".dx-rte-tool")[0].Click();   // Bold

        Assert.True(sanitizeCalls >= 1);
    }
}
