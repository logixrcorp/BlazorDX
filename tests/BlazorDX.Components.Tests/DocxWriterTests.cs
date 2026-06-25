using System.IO.Compression;
using BlazorDX.Documents;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The C# .docx writer: round-trips the <see cref="WordDocument"/> model through
/// <c>DocxReader.Read(DocxWriter.Write(doc))</c>, asserting the writer emits exactly
/// the WordprocessingML the reader resolves. Headings (1-6 and Title), bold/italic
/// runs, bulleted/numbered lists, and a header-row table must all survive the trip.
/// </summary>
public sealed class DocxWriterTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void RoundTrips_each_heading_level(int level)
    {
        WordDocument doc = new([new WordHeading(level, [new WordRun($"Heading {level}")])]);

        WordDocument result = RoundTrip(doc);

        WordHeading heading = Assert.IsType<WordHeading>(Assert.Single(result.Blocks));
        Assert.Equal(level, heading.Level);
        Assert.Equal($"Heading {level}", PlainText(heading.Runs));
    }

    [Fact]
    public void RoundTrips_a_title_as_level_1_heading()
    {
        // The model has no distinct "Title" block; a level-1 heading is the closest
        // construct and the writer/reader agree on it. (Title style maps to level 1.)
        WordDocument doc = new([new WordHeading(1, [new WordRun("The Title")])]);

        WordDocument result = RoundTrip(doc);

        WordHeading heading = Assert.IsType<WordHeading>(Assert.Single(result.Blocks));
        Assert.Equal(1, heading.Level);
        Assert.Equal("The Title", PlainText(heading.Runs));
    }

    [Fact]
    public void RoundTrips_a_paragraph_with_mixed_bold_and_italic_runs()
    {
        WordDocument doc = new(
        [
            new WordParagraph(
            [
                new WordRun("plain ", Bold: false, Italic: false),
                new WordRun("bold", Bold: true, Italic: false),
                new WordRun(" and ", Bold: false, Italic: false),
                new WordRun("italic", Bold: false, Italic: true),
                new WordRun(" and both", Bold: true, Italic: true),
            ]),
        ]);

        WordDocument result = RoundTrip(doc);

        WordParagraph para = Assert.IsType<WordParagraph>(Assert.Single(result.Blocks));
        Assert.Equal(5, para.Runs.Count);

        Assert.Equal(new WordRun("plain ", false, false), para.Runs[0]);
        Assert.Equal(new WordRun("bold", true, false), para.Runs[1]);
        Assert.Equal(new WordRun(" and ", false, false), para.Runs[2]);
        Assert.Equal(new WordRun("italic", false, true), para.Runs[3]);
        Assert.Equal(new WordRun(" and both", true, true), para.Runs[4]);
    }

    [Fact]
    public void RoundTrips_a_bullet_list()
    {
        WordDocument doc = new(
        [
            new WordList(Ordered: false,
            [
                [new WordRun("Apples")],
                [new WordRun("Oranges")],
                [new WordRun("Pears")],
            ]),
        ]);

        WordDocument result = RoundTrip(doc);

        WordList list = Assert.IsType<WordList>(Assert.Single(result.Blocks));
        Assert.False(list.Ordered);
        Assert.Equal(["Apples", "Oranges", "Pears"], list.Items.Select(PlainText).ToArray());
    }

    [Fact]
    public void RoundTrips_a_numbered_list()
    {
        WordDocument doc = new(
        [
            new WordList(Ordered: true,
            [
                [new WordRun("First")],
                [new WordRun("Second")],
            ]),
        ]);

        WordDocument result = RoundTrip(doc);

        WordList list = Assert.IsType<WordList>(Assert.Single(result.Blocks));
        Assert.True(list.Ordered);
        Assert.Equal(["First", "Second"], list.Items.Select(PlainText).ToArray());
    }

    [Fact]
    public void RoundTrips_a_table_with_a_header_row_and_body_rows()
    {
        WordDocument doc = new(
        [
            new WordTable(
            [
                new WordTableRow([Cell("Name"), Cell("Role")]),
                new WordTableRow([Cell("Ada"), Cell("Analyst")]),
                new WordTableRow([Cell("Grace"), Cell("Admiral")]),
            ]),
        ]);

        WordDocument result = RoundTrip(doc);

        WordTable table = Assert.IsType<WordTable>(Assert.Single(result.Blocks));
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(["Name", "Role"], table.Rows[0].Cells.Select(c => PlainText(c.Runs)).ToArray());
        Assert.Equal(["Ada", "Analyst"], table.Rows[1].Cells.Select(c => PlainText(c.Runs)).ToArray());
        Assert.Equal(["Grace", "Admiral"], table.Rows[2].Cells.Select(c => PlainText(c.Runs)).ToArray());
    }

    [Fact]
    public void RoundTrips_a_mixed_document_in_order()
    {
        WordDocument doc = new(
        [
            new WordHeading(1, [new WordRun("Report")]),
            new WordParagraph([new WordRun("An "), new WordRun("important", Bold: true), new WordRun(" intro.")]),
            new WordHeading(2, [new WordRun("Findings")]),
            new WordList(Ordered: false, [[new WordRun("Point one")], [new WordRun("Point two")]]),
            new WordList(Ordered: true, [[new WordRun("Step A")], [new WordRun("Step B")]]),
            new WordTable(
            [
                new WordTableRow([Cell("Metric"), Cell("Value")]),
                new WordTableRow([Cell("Users"), Cell("42")]),
            ]),
            new WordParagraph([new WordRun("The end.")]),
        ]);

        WordDocument result = RoundTrip(doc);

        Assert.Equal(7, result.Blocks.Count);

        Assert.Equal(1, Assert.IsType<WordHeading>(result.Blocks[0]).Level);
        Assert.Equal("Report", PlainText(((WordHeading)result.Blocks[0]).Runs));

        WordParagraph intro = Assert.IsType<WordParagraph>(result.Blocks[1]);
        Assert.Equal(3, intro.Runs.Count);
        Assert.True(intro.Runs[1].Bold);
        Assert.Equal("important", intro.Runs[1].Text);

        Assert.Equal(2, Assert.IsType<WordHeading>(result.Blocks[2]).Level);

        WordList bullets = Assert.IsType<WordList>(result.Blocks[3]);
        Assert.False(bullets.Ordered);
        Assert.Equal(["Point one", "Point two"], bullets.Items.Select(PlainText).ToArray());

        WordList numbered = Assert.IsType<WordList>(result.Blocks[4]);
        Assert.True(numbered.Ordered);
        Assert.Equal(["Step A", "Step B"], numbered.Items.Select(PlainText).ToArray());

        WordTable table = Assert.IsType<WordTable>(result.Blocks[5]);
        Assert.Equal(["Metric", "Value"], table.Rows[0].Cells.Select(c => PlainText(c.Runs)).ToArray());
        Assert.Equal(["Users", "42"], table.Rows[1].Cells.Select(c => PlainText(c.Runs)).ToArray());

        Assert.Equal("The end.", PlainText(Assert.IsType<WordParagraph>(result.Blocks[6]).Runs));
    }

    [Fact]
    public void RoundTrips_text_needing_xml_escaping()
    {
        WordDocument doc = new(
        [
            new WordParagraph([new WordRun("a < b && c > d \"quoted\"")]),
        ]);

        WordDocument result = RoundTrip(doc);

        WordParagraph para = Assert.IsType<WordParagraph>(Assert.Single(result.Blocks));
        Assert.Equal("a < b && c > d \"quoted\"", PlainText(para.Runs));
    }

    [Fact]
    public void Output_is_a_valid_zip_with_the_expected_parts()
    {
        byte[] bytes = DocxWriter.Write(new WordDocument([new WordParagraph([new WordRun("hi")])]));

        using MemoryStream stream = new(bytes, writable: false);
        using ZipArchive zip = new(stream, ZipArchiveMode.Read);

        foreach (string part in new[]
        {
            "[Content_Types].xml",
            "_rels/.rels",
            "word/_rels/document.xml.rels",
            "word/document.xml",
            "word/styles.xml",
            "word/numbering.xml",
        })
        {
            Assert.NotNull(zip.GetEntry(part));
        }
    }

    private static WordTableCell Cell(string text) => new([new WordRun(text)]);

    private static string PlainText(IReadOnlyList<WordRun> runs) =>
        string.Concat(runs.Select(r => r.Text));

    private static WordDocument RoundTrip(WordDocument doc) =>
        DocxReader.Read(DocxWriter.Write(doc));
}
