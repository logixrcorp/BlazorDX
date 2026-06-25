using System.Text;
using BlazorDX.Documents;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The bidirectional <see cref="WordHtml"/> mapper that backs the Word editor. Tests cover
/// the round-trip contract (<c>FromHtml(ToHtml(doc))</c> reproduces structure), output
/// escaping, parser leniency on messy contentEditable markup, and the full editor→save
/// chain through <see cref="DocxWriter"/>/<see cref="DocxReader"/>.
/// </summary>
public sealed class WordHtmlTests
{
    // ------------------------------------------------------------------
    // Round-trip: FromHtml(ToHtml(doc)) reproduces structure
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void RoundTrips_each_heading_level(int level)
    {
        WordDocument doc = new([new WordHeading(level, [new WordRun("Section")])]);

        WordDocument back = WordHtml.FromHtml(WordHtml.ToHtml(doc));

        WordHeading heading = Assert.IsType<WordHeading>(Assert.Single(back.Blocks));
        Assert.Equal(level, heading.Level);
        Assert.Equal("Section", PlainText(heading.Runs));
    }

    [Fact]
    public void RoundTrips_paragraph_with_plain_bold_italic_and_both_runs()
    {
        WordDocument doc = new(
        [
            new WordParagraph(
            [
                new WordRun("plain "),
                new WordRun("bold", Bold: true),
                new WordRun(" and "),
                new WordRun("italic", Italic: true),
                new WordRun(" and "),
                new WordRun("both", Bold: true, Italic: true),
            ]),
        ]);

        WordDocument back = WordHtml.FromHtml(WordHtml.ToHtml(doc));

        WordParagraph para = Assert.IsType<WordParagraph>(Assert.Single(back.Blocks));
        Assert.Equal(6, para.Runs.Count);

        Assert.Equal(("plain ", false, false), Shape(para.Runs[0]));
        Assert.Equal(("bold", true, false), Shape(para.Runs[1]));
        Assert.Equal((" and ", false, false), Shape(para.Runs[2]));
        Assert.Equal(("italic", false, true), Shape(para.Runs[3]));
        Assert.Equal((" and ", false, false), Shape(para.Runs[4]));
        Assert.Equal(("both", true, true), Shape(para.Runs[5]));
    }

    [Fact]
    public void RoundTrips_bullet_list()
    {
        WordDocument doc = new(
        [
            new WordList(Ordered: false,
            [
                [new WordRun("first")],
                [new WordRun("second")],
                [new WordRun("third")],
            ]),
        ]);

        WordDocument back = WordHtml.FromHtml(WordHtml.ToHtml(doc));

        WordList list = Assert.IsType<WordList>(Assert.Single(back.Blocks));
        Assert.False(list.Ordered);
        Assert.Equal(["first", "second", "third"], list.Items.Select(PlainText));
    }

    [Fact]
    public void RoundTrips_numbered_list()
    {
        WordDocument doc = new(
        [
            new WordList(Ordered: true,
            [
                [new WordRun("one")],
                [new WordRun("two")],
            ]),
        ]);

        WordDocument back = WordHtml.FromHtml(WordHtml.ToHtml(doc));

        WordList list = Assert.IsType<WordList>(Assert.Single(back.Blocks));
        Assert.True(list.Ordered);
        Assert.Equal(["one", "two"], list.Items.Select(PlainText));
    }

    [Fact]
    public void RoundTrips_table_with_header_and_body_rows()
    {
        WordDocument doc = new(
        [
            new WordTable(
            [
                new WordTableRow([Cell("Name"), Cell("Role")]),
                new WordTableRow([Cell("Ada"), Cell("Engineer")]),
                new WordTableRow([Cell("Grace"), Cell("Admiral")]),
            ]),
        ]);

        WordDocument back = WordHtml.FromHtml(WordHtml.ToHtml(doc));

        WordTable table = Assert.IsType<WordTable>(Assert.Single(back.Blocks));
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(["Name", "Role"], table.Rows[0].Cells.Select(c => PlainText(c.Runs)));
        Assert.Equal(["Ada", "Engineer"], table.Rows[1].Cells.Select(c => PlainText(c.Runs)));
        Assert.Equal(["Grace", "Admiral"], table.Rows[2].Cells.Select(c => PlainText(c.Runs)));
    }

    [Fact]
    public void Table_header_row_renders_as_th_scope_col()
    {
        WordDocument doc = new(
        [
            new WordTable(
            [
                new WordTableRow([Cell("H1"), Cell("H2")]),
                new WordTableRow([Cell("a"), Cell("b")]),
            ]),
        ]);

        string html = WordHtml.ToHtml(doc);

        Assert.Contains("<th scope=\"col\">H1</th>", html, StringComparison.Ordinal);
        Assert.Contains("<td>a</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrips_mixed_document_in_order()
    {
        WordDocument doc = new(
        [
            new WordHeading(1, [new WordRun("Title")]),
            new WordParagraph([new WordRun("Intro "), new WordRun("bold", Bold: true)]),
            new WordList(Ordered: false, [[new WordRun("a")], [new WordRun("b")]]),
            new WordHeading(2, [new WordRun("Subsection")]),
            new WordList(Ordered: true, [[new WordRun("1")], [new WordRun("2")]]),
            new WordTable(
            [
                new WordTableRow([Cell("Col")]),
                new WordTableRow([Cell("val")]),
            ]),
            new WordParagraph([new WordRun("Outro")]),
        ]);

        WordDocument back = WordHtml.FromHtml(WordHtml.ToHtml(doc));

        Assert.Equal(7, back.Blocks.Count);
        Assert.Equal(1, Assert.IsType<WordHeading>(back.Blocks[0]).Level);
        Assert.Equal("Intro bold", PlainText(((WordParagraph)back.Blocks[1]).Runs));
        Assert.False(Assert.IsType<WordList>(back.Blocks[2]).Ordered);
        Assert.Equal(2, Assert.IsType<WordHeading>(back.Blocks[3]).Level);
        Assert.True(Assert.IsType<WordList>(back.Blocks[4]).Ordered);
        WordTable table = Assert.IsType<WordTable>(back.Blocks[5]);
        Assert.Equal("Col", PlainText(table.Rows[0].Cells[0].Runs));
        Assert.Equal("Outro", PlainText(((WordParagraph)back.Blocks[6]).Runs));
    }

    // ------------------------------------------------------------------
    // ToHtml escaping
    // ------------------------------------------------------------------

    [Fact]
    public void ToHtml_escapes_text_so_markup_cannot_be_injected()
    {
        WordDocument doc = new(
        [
            new WordParagraph([new WordRun("<script>alert('x & y')</script>")]),
        ]);

        string html = WordHtml.ToHtml(doc);

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&amp;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_escaped_text_decodes_back_on_round_trip()
    {
        WordDocument doc = new(
        [
            new WordParagraph([new WordRun("a < b && c > d \"q\"")]),
        ]);

        WordDocument back = WordHtml.FromHtml(WordHtml.ToHtml(doc));

        Assert.Equal("a < b && c > d \"q\"", PlainText(((WordParagraph)Assert.Single(back.Blocks)).Runs));
    }

    [Fact]
    public void ToHtml_empty_document_is_empty_string()
    {
        Assert.Equal(string.Empty, WordHtml.ToHtml(new WordDocument([])));
    }

    // ------------------------------------------------------------------
    // FromHtml leniency
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t  ")]
    [InlineData("<p></p>")]
    [InlineData("<!-- just a comment -->")]
    public void FromHtml_empty_or_whitespace_input_yields_empty_document(string? html)
    {
        Assert.Empty(WordHtml.FromHtml(html).Blocks);
    }

    [Fact]
    public void FromHtml_does_not_throw_on_garbage()
    {
        WordDocument doc = WordHtml.FromHtml("<<<>>> <p attr=\"<broken </ <h9> &notanentity; <td>");
        Assert.NotNull(doc); // best-effort, no crash
    }

    [Fact]
    public void FromHtml_accepts_uppercase_and_mixed_case_tags()
    {
        WordDocument doc = WordHtml.FromHtml("<H2>Heading</H2><P>Body <STRONG>x</STRONG></P>");

        Assert.Equal(2, doc.Blocks.Count);
        Assert.Equal(2, Assert.IsType<WordHeading>(doc.Blocks[0]).Level);
        WordParagraph para = Assert.IsType<WordParagraph>(doc.Blocks[1]);
        Assert.Equal("Body x", PlainText(para.Runs));
        Assert.True(para.Runs[1].Bold);
    }

    [Fact]
    public void FromHtml_treats_b_and_i_as_strong_and_em()
    {
        WordDocument doc = WordHtml.FromHtml("<p><b>bold</b><i>italic</i><b><i>both</i></b></p>");

        WordParagraph para = Assert.IsType<WordParagraph>(Assert.Single(doc.Blocks));
        Assert.Equal(("bold", true, false), Shape(para.Runs[0]));
        Assert.Equal(("italic", false, true), Shape(para.Runs[1]));
        Assert.Equal(("both", true, true), Shape(para.Runs[2]));
    }

    [Fact]
    public void FromHtml_treats_div_as_paragraph_break()
    {
        WordDocument doc = WordHtml.FromHtml("<div>line one</div><div>line two</div>");

        Assert.Equal(2, doc.Blocks.Count);
        Assert.Equal("line one", PlainText(((WordParagraph)doc.Blocks[0]).Runs));
        Assert.Equal("line two", PlainText(((WordParagraph)doc.Blocks[1]).Runs));
    }

    [Fact]
    public void FromHtml_treats_br_as_paragraph_break()
    {
        WordDocument doc = WordHtml.FromHtml("first<br>second<br/>third");

        Assert.Equal(3, doc.Blocks.Count);
        Assert.Equal("first", PlainText(((WordParagraph)doc.Blocks[0]).Runs));
        Assert.Equal("second", PlainText(((WordParagraph)doc.Blocks[1]).Runs));
        Assert.Equal("third", PlainText(((WordParagraph)doc.Blocks[2]).Runs));
    }

    [Fact]
    public void FromHtml_handles_unclosed_paragraph_and_emphasis()
    {
        // contentEditable frequently emits unbalanced markup; we flush at end of input.
        WordDocument doc = WordHtml.FromHtml("<p>open <strong>still bold");

        WordParagraph para = Assert.IsType<WordParagraph>(Assert.Single(doc.Blocks));
        Assert.Equal("open still bold", PlainText(para.Runs));
        Assert.False(para.Runs[0].Bold);
        Assert.True(para.Runs[1].Bold);
    }

    [Fact]
    public void FromHtml_decodes_named_and_numeric_entities()
    {
        WordDocument doc = WordHtml.FromHtml("<p>&lt;a&gt; &amp; b &#65; &#x42; &nbsp;end</p>");

        WordParagraph para = Assert.IsType<WordParagraph>(Assert.Single(doc.Blocks));
        Assert.Equal("<a> & b A B  end", PlainText(para.Runs)); // &nbsp; -> U+00A0
    }

    [Fact]
    public void FromHtml_degrades_unknown_tags_to_their_text_content()
    {
        WordDocument doc = WordHtml.FromHtml(
            "<p>see <a href=\"http://x\">link</a> and <span class=\"c\">span</span> <font>f</font></p>");

        WordParagraph para = Assert.IsType<WordParagraph>(Assert.Single(doc.Blocks));
        Assert.Equal("see link and span f", PlainText(para.Runs));
        Assert.All(para.Runs, r => Assert.False(r.Bold || r.Italic));
    }

    [Fact]
    public void FromHtml_bare_text_becomes_a_paragraph()
    {
        WordDocument doc = WordHtml.FromHtml("just some loose text");

        WordParagraph para = Assert.IsType<WordParagraph>(Assert.Single(doc.Blocks));
        Assert.Equal("just some loose text", PlainText(para.Runs));
    }

    [Fact]
    public void FromHtml_parses_list_without_explicit_li_close()
    {
        WordDocument doc = WordHtml.FromHtml("<ul><li>a<li>b<li>c</ul>");

        WordList list = Assert.IsType<WordList>(Assert.Single(doc.Blocks));
        Assert.False(list.Ordered);
        Assert.Equal(["a", "b", "c"], list.Items.Select(PlainText));
    }

    // ------------------------------------------------------------------
    // FromHtml never throws + bounds pathological input (hardening)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("<>")]                       // empty tag: close at/right after open
    [InlineData("<")]                        // bare unterminated '<'
    [InlineData("a<>b")]                     // empty tag between text
    [InlineData("<><><>")]                   // several empty tags
    [InlineData("<<>>")]                     // nested angle brackets
    public void FromHtml_malformed_empty_tags_never_throw(string html)
    {
        WordDocument doc = WordHtml.FromHtml(html);
        Assert.NotNull(doc); // the <> case previously threw ArgumentOutOfRangeException
    }

    [Theory]
    [InlineData("<p>open")]                  // unclosed paragraph
    [InlineData("<strong>bold forever")]     // unclosed emphasis
    [InlineData("<ul><li>item")]             // unclosed list/item
    [InlineData("<table><tr><td>cell")]      // unclosed table
    public void FromHtml_unclosed_tags_never_throw(string html)
    {
        WordDocument doc = WordHtml.FromHtml(html);
        Assert.NotNull(doc);
    }

    [Fact]
    public void FromHtml_deeply_nested_emphasis_returns_a_document_without_throwing()
    {
        // <strong> x N then text then N closes. Pathological nesting must not blow up.
        const int n = 100_000;
        StringBuilder sb = new(n * 9);
        sb.Append("<p>");
        for (int k = 0; k < n; k++)
        {
            sb.Append("<strong>");
        }

        sb.Append("deep");
        for (int k = 0; k < n; k++)
        {
            sb.Append("</strong>");
        }

        sb.Append("</p>");

        WordDocument doc = WordHtml.FromHtml(sb.ToString());

        WordParagraph para = Assert.IsType<WordParagraph>(Assert.Single(doc.Blocks));
        Assert.Equal("deep", PlainText(para.Runs));
        Assert.True(para.Runs[0].Bold);
    }

    [Fact]
    public void FromHtml_very_large_input_returns_a_document_without_throwing()
    {
        // A multi-megabyte hostile blob must degrade gracefully, not OOM or hang.
        string huge = new string('x', 5_000_000);
        WordDocument doc = WordHtml.FromHtml(huge);

        WordParagraph para = Assert.IsType<WordParagraph>(Assert.Single(doc.Blocks));
        Assert.Equal(huge.Length, PlainText(para.Runs).Length);
    }

    [Fact]
    public void FromHtml_huge_tag_count_is_bounded_and_does_not_throw()
    {
        // Millions of tiny tags exceed the token cap; the result is still a valid,
        // best-effort document produced in linear time.
        StringBuilder sb = new();
        for (int k = 0; k < 2_000_000; k++)
        {
            sb.Append("<b>");
        }

        sb.Append("tail");

        WordDocument doc = WordHtml.FromHtml(sb.ToString());
        Assert.NotNull(doc);
    }

    // ------------------------------------------------------------------
    // Bonus: full chain HTML-edit -> save .docx round-trips
    // ------------------------------------------------------------------

    [Fact]
    public void FullChain_html_edit_then_save_docx_preserves_structure()
    {
        WordDocument doc = new(
        [
            new WordHeading(1, [new WordRun("Report")]),
            new WordParagraph([new WordRun("Body "), new WordRun("emph", Italic: true)]),
            new WordList(Ordered: true, [[new WordRun("step one")], [new WordRun("step two")]]),
            new WordTable(
            [
                new WordTableRow([Cell("K"), Cell("V")]),
                new WordTableRow([Cell("x"), Cell("1")]),
            ]),
        ]);

        // HTML edit -> model -> .docx -> model
        WordDocument fromHtml = WordHtml.FromHtml(WordHtml.ToHtml(doc));
        WordDocument back = DocxReader.Read(DocxWriter.Write(fromHtml));

        Assert.Equal(4, back.Blocks.Count);
        Assert.Equal(1, Assert.IsType<WordHeading>(back.Blocks[0]).Level);
        Assert.Equal("Report", PlainText(((WordHeading)back.Blocks[0]).Runs));

        WordParagraph para = Assert.IsType<WordParagraph>(back.Blocks[1]);
        Assert.Equal("Body emph", PlainText(para.Runs));
        Assert.True(para.Runs[^1].Italic);

        WordList list = Assert.IsType<WordList>(back.Blocks[2]);
        Assert.True(list.Ordered);
        Assert.Equal(["step one", "step two"], list.Items.Select(PlainText));

        WordTable table = Assert.IsType<WordTable>(back.Blocks[3]);
        Assert.Equal(["K", "V"], table.Rows[0].Cells.Select(c => PlainText(c.Runs)));
        Assert.Equal(["x", "1"], table.Rows[1].Cells.Select(c => PlainText(c.Runs)));
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private static WordTableCell Cell(string text) => new([new WordRun(text)]);

    private static (string Text, bool Bold, bool Italic) Shape(WordRun run) =>
        (run.Text, run.Bold, run.Italic);

    private static string PlainText(IReadOnlyList<WordRun> runs)
    {
        StringBuilder sb = new();
        foreach (WordRun run in runs)
        {
            sb.Append(run.Text);
        }

        return sb.ToString();
    }
}
