using BlazorDX.Components;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>The split editor: source textarea drives a live sanitized preview.</summary>
public sealed class DxMarkdownEditorTests : TestContext
{
    [Fact]
    public void Renders_source_textarea_and_preview()
    {
        IRenderedComponent<DxMarkdownEditor> editor = RenderComponent<DxMarkdownEditor>(parameters => parameters
            .Add(e => e.Value, "# Hi"));

        Assert.NotNull(editor.Find("textarea.dx-md-source"));
        Assert.Contains("<h1>Hi</h1>", editor.Find(".dx-md-preview").InnerHtml);
    }

    [Fact]
    public void Typing_updates_the_bound_value()
    {
        string? bound = null;
        IRenderedComponent<DxMarkdownEditor> editor = RenderComponent<DxMarkdownEditor>(parameters => parameters
            .Add(e => e.Value, string.Empty)
            .Add(e => e.ValueChanged, v => bound = v));

        editor.Find("textarea.dx-md-source").Input("**bold**");

        Assert.Equal("**bold**", bound);
    }
}
