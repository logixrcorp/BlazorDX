using BlazorDX.Components;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>The Markdown subset, with the security guarantees front and center.</summary>
public sealed class MarkdownRendererTests
{
    private static string Render(string markdown) => MarkdownRenderer.Render(markdown).Value;

    [Fact]
    public void Headings_map_to_h_tags_by_level()
    {
        Assert.Contains("<h1>Title</h1>", Render("# Title"));
        Assert.Contains("<h3>Sub</h3>", Render("### Sub"));
    }

    [Fact]
    public void Bold_italic_and_code_format_inline()
    {
        string html = Render("a **b** _c_ `d`");
        Assert.Contains("<strong>b</strong>", html);
        Assert.Contains("<em>c</em>", html);
        Assert.Contains("<code>d</code>", html);
    }

    [Fact]
    public void Unordered_and_ordered_lists_render()
    {
        Assert.Contains("<ul><li>one</li><li>two</li></ul>", Render("- one\n- two"));
        Assert.Contains("<ol><li>first</li></ol>", Render("1. first"));
    }

    [Fact]
    public void Fenced_code_block_is_preformatted_and_literal()
    {
        string html = Render("```\nvar x = 1 < 2;\n```");
        Assert.Contains("<pre><code>", html);
        Assert.Contains("var x = 1 &lt; 2;", html);   // encoded, not interpreted
    }

    [Fact]
    public void Safe_links_render_with_rel_and_unsafe_links_are_stripped()
    {
        Assert.Contains("<a href=\"https://example.com\" rel=\"noopener noreferrer\">site</a>",
            Render("[site](https://example.com)"));

        string js = Render("[click](javascript:alert(1))");
        Assert.DoesNotContain("<a", js);          // link dropped
        Assert.Contains("click", js);              // text preserved
    }

    [Fact]
    public void Raw_html_is_encoded_inert()
    {
        string html = Render("a <script>alert('x')</script> b");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Code_span_contents_are_not_treated_as_html_or_formatting()
    {
        string html = Render("`<b>**not bold**</b>`");
        Assert.Contains("<code>&lt;b&gt;**not bold**&lt;/b&gt;</code>", html);
        Assert.DoesNotContain("<strong>", html);
    }

    [Fact]
    public void Render_returns_a_markup_string()
    {
        MarkupString result = MarkdownRenderer.Render("# Hi");
        Assert.Contains("<h1>Hi</h1>", result.Value);
    }

    [Fact]
    public void Scheme_relative_links_are_treated_as_unsafe_but_real_relative_links_render()
    {
        // "//evil.com" looks relative but resolves off-site, so the link is dropped (text kept)...
        string scheme = Render("[x](//evil.com)");
        Assert.DoesNotContain("<a", scheme);
        Assert.Contains("x", scheme);

        // ...while a genuine relative path still renders a link.
        Assert.Contains("<a href=\"/path\"", Render("[x](/path)"));
    }
}
