using AngleSharp.Dom;
using BlazorDX.Documents;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The read-only Word viewer: asserts the parsed model renders as <b>real semantic
/// elements</b> — a true heading hierarchy, lists as lists, emphasis as
/// <c>&lt;strong&gt;</c>/<c>&lt;em&gt;</c>, and a table with <c>&lt;th scope&gt;</c> —
/// in document order. No MarkupString: text is HTML-encoded by AddContent.
/// </summary>
public sealed class DxWordViewerTests : TestContext
{
    private static WordDocument SampleDocument() =>
        new(
        [
            new WordHeading(1, [new WordRun("Quarterly Report")]),
            new WordHeading(2, [new WordRun("Overview")]),
            new WordParagraph(
            [
                new WordRun("This quarter was "),
                new WordRun("strong", Bold: true),
                new WordRun(" and "),
                new WordRun("steady", Italic: true),
                new WordRun("."),
            ]),
            new WordList(false, [[new WordRun("First point")], [new WordRun("Second point")]]),
            new WordList(true, [[new WordRun("Step one")], [new WordRun("Step two")]]),
            new WordTable(
            [
                new WordTableRow([new WordTableCell([new WordRun("Name")]), new WordTableCell([new WordRun("Role")])]),
                new WordTableRow([new WordTableCell([new WordRun("Ada")]), new WordTableCell([new WordRun("Analyst")])]),
            ]),
        ]);

    private IRenderedComponent<DxWordViewer> Render(WordDocument document) =>
        RenderComponent<DxWordViewer>(p => p.Add(v => v.Document, document));

    [Fact]
    public void Renders_headings_as_real_heading_elements()
    {
        IRenderedComponent<DxWordViewer> viewer = Render(SampleDocument());

        IElement h1 = viewer.Find("h1");
        Assert.Equal("Quarterly Report", h1.TextContent);

        IElement h2 = viewer.Find("h2");
        Assert.Equal("Overview", h2.TextContent);
    }

    [Fact]
    public void Bold_and_italic_runs_render_as_strong_and_em()
    {
        IRenderedComponent<DxWordViewer> viewer = Render(SampleDocument());

        IElement para = viewer.Find("p.dx-word-para");
        Assert.Equal("strong", para.QuerySelector("strong")!.TextContent);
        Assert.Equal("steady", para.QuerySelector("em")!.TextContent);
        // Plain text is not wrapped in a fake element.
        Assert.Contains("This quarter was", para.TextContent);
    }

    [Fact]
    public void Bullet_list_renders_as_ul_and_numbered_list_as_ol()
    {
        IRenderedComponent<DxWordViewer> viewer = Render(SampleDocument());

        IElement ul = viewer.Find("ul.dx-word-list");
        Assert.Equal(2, ul.QuerySelectorAll("li").Length);
        Assert.Equal("First point", ul.QuerySelectorAll("li")[0].TextContent);

        IElement ol = viewer.Find("ol.dx-word-list");
        Assert.Equal(2, ol.QuerySelectorAll("li").Length);
        Assert.Equal("Step two", ol.QuerySelectorAll("li")[1].TextContent);
    }

    [Fact]
    public void Table_header_row_uses_th_with_col_scope()
    {
        IRenderedComponent<DxWordViewer> viewer = Render(SampleDocument());

        IElement table = viewer.Find("table.dx-word-table");

        IHtmlCollection<IElement> headers = table.QuerySelectorAll("thead th");
        Assert.Equal(2, headers.Length);
        Assert.All(headers, th => Assert.Equal("col", th.GetAttribute("scope")));
        Assert.Equal(["Name", "Role"], headers.Select(h => h.TextContent).ToArray());

        IHtmlCollection<IElement> dataCells = table.QuerySelectorAll("tbody td");
        Assert.Equal(["Ada", "Analyst"], dataCells.Select(c => c.TextContent).ToArray());
    }

    [Fact]
    public void Document_region_is_focusable_and_labelled()
    {
        IRenderedComponent<DxWordViewer> viewer =
            RenderComponent<DxWordViewer>(p => p
                .Add(v => v.Document, SampleDocument())
                .Add(v => v.Label, "Quarterly report"));

        IElement region = viewer.Find(".dx-word-viewer");
        Assert.Equal("document", region.GetAttribute("role"));
        Assert.Equal("0", region.GetAttribute("tabindex"));
        Assert.Equal("Quarterly report", region.GetAttribute("aria-label"));
    }

    [Fact]
    public void Blocks_render_in_document_order()
    {
        IRenderedComponent<DxWordViewer> viewer = Render(SampleDocument());

        // The first heading must precede the paragraph, which precedes the lists/table.
        string markup = viewer.Markup;
        int h1 = markup.IndexOf("Quarterly Report", StringComparison.Ordinal);
        int para = markup.IndexOf("This quarter was", StringComparison.Ordinal);
        int table = markup.IndexOf("Analyst", StringComparison.Ordinal);
        Assert.True(h1 >= 0 && h1 < para && para < table);
    }

    [Fact]
    public void Text_is_html_encoded_not_raw_markup()
    {
        WordDocument doc = new([new WordParagraph([new WordRun("<script>alert(1)</script>")])]);
        IRenderedComponent<DxWordViewer> viewer = Render(doc);

        // No live <script>; the angle brackets are encoded in the rendered markup.
        Assert.Empty(viewer.FindAll("script"));
        Assert.Contains("&lt;script&gt;", viewer.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_document_shows_a_message()
    {
        IRenderedComponent<DxWordViewer> viewer = Render(new WordDocument([]));
        Assert.Contains("No document", viewer.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(viewer.FindAll("h1"));
    }

    [Fact]
    public void Renders_a_nested_list_as_a_real_nested_ul()
    {
        // A (top) > B (nested) > C (back to top) renders <ul>…<ul>…</ul>…</ul>.
        WordDocument doc = new(
        [
            new WordList(false,
                [[new WordRun("A")], [new WordRun("B")], [new WordRun("C")]],
                Levels: [0, 1, 0]),
        ]);

        IRenderedComponent<DxWordViewer> viewer = Render(doc);

        // The nested <ul> lives inside the first <li> (the parent item, A).
        IElement firstItem = viewer.FindAll("ul > li")[0];
        Assert.Single(firstItem.QuerySelectorAll("ul"));
        Assert.Contains("B", firstItem.QuerySelector("ul li")!.TextContent);
        Assert.Equal(3, viewer.FindAll("li").Count); // three items across both levels
    }
}
