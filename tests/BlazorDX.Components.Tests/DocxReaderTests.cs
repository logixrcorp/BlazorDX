using System.IO.Compression;
using System.Text;
using BlazorDX.Documents;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The C# .docx reader: hand-built minimal OOXML word-processing packages (the inverse
/// of how XlsxReader tests build input) exercise headings, bold/italic runs, bullet and
/// numbered lists, and a table with a header row. The test owns exactly what the reader
/// sees — document.xml plus, where heading-style resolution matters, styles.xml.
/// </summary>
public sealed class DocxReaderTests
{
    private const string WordprocessingMl =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void Reads_headings_at_their_style_level()
    {
        WordDocument doc = ReadBody(
            """
            <w:p><w:pPr><w:pStyle w:val="Title"/></w:pPr><w:r><w:t>The Title</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Section One</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="Heading2"/></w:pPr><w:r><w:t>Subsection</w:t></w:r></w:p>
            """);

        Assert.Equal(3, doc.Blocks.Count);
        WordHeading title = Assert.IsType<WordHeading>(doc.Blocks[0]);
        Assert.Equal(1, title.Level); // Title -> level 1
        Assert.Equal("The Title", PlainText(title.Runs));

        Assert.Equal(1, Assert.IsType<WordHeading>(doc.Blocks[1]).Level);
        Assert.Equal(2, Assert.IsType<WordHeading>(doc.Blocks[2]).Level);
        Assert.Equal("Subsection", PlainText(((WordHeading)doc.Blocks[2]).Runs));
    }

    [Fact]
    public void Resolves_localized_heading_style_via_styles_xml()
    {
        // A custom style id that is NOT "HeadingN"; styles.xml maps it via outlineLvl.
        string styles =
            """
            <w:style w:type="paragraph" w:styleId="berschrift1">
              <w:name w:val="heading 1"/><w:outlineLvl w:val="0"/>
            </w:style>
            """;

        WordDocument doc = ReadBody(
            """<w:p><w:pPr><w:pStyle w:val="berschrift1"/></w:pPr><w:r><w:t>Localized</w:t></w:r></w:p>""",
            styles);

        WordHeading heading = Assert.IsType<WordHeading>(Assert.Single(doc.Blocks));
        Assert.Equal(1, heading.Level);
        Assert.Equal("Localized", PlainText(heading.Runs));
    }

    [Fact]
    public void Reads_paragraph_with_bold_and_italic_runs()
    {
        WordDocument doc = ReadBody(
            """
            <w:p>
              <w:r><w:t xml:space="preserve">plain </w:t></w:r>
              <w:r><w:rPr><w:b/></w:rPr><w:t>bold</w:t></w:r>
              <w:r><w:t xml:space="preserve"> and </w:t></w:r>
              <w:r><w:rPr><w:i/></w:rPr><w:t>italic</w:t></w:r>
            </w:p>
            """);

        WordParagraph para = Assert.IsType<WordParagraph>(Assert.Single(doc.Blocks));
        Assert.Equal(4, para.Runs.Count);

        Assert.Equal("plain ", para.Runs[0].Text);
        Assert.False(para.Runs[0].Bold);
        Assert.False(para.Runs[0].Italic);

        Assert.Equal("bold", para.Runs[1].Text);
        Assert.True(para.Runs[1].Bold);
        Assert.False(para.Runs[1].Italic);

        Assert.Equal("italic", para.Runs[3].Text);
        Assert.True(para.Runs[3].Italic);
        Assert.False(para.Runs[3].Bold);
    }

    [Fact]
    public void Coalesces_adjacent_runs_with_the_same_formatting()
    {
        // Word often splits a single word into several runs on rsid boundaries.
        WordDocument doc = ReadBody(
            """
            <w:p>
              <w:r><w:t>Hel</w:t></w:r>
              <w:r><w:t>lo</w:t></w:r>
              <w:r><w:rPr><w:b/></w:rPr><w:t>World</w:t></w:r>
            </w:p>
            """);

        WordParagraph para = Assert.IsType<WordParagraph>(Assert.Single(doc.Blocks));
        Assert.Equal(2, para.Runs.Count); // "Hello" merged; bold "World" separate
        Assert.Equal("Hello", para.Runs[0].Text);
        Assert.Equal("World", para.Runs[1].Text);
        Assert.True(para.Runs[1].Bold);
    }

    [Fact]
    public void Reads_a_bullet_list_then_a_numbered_list()
    {
        // numId 0 => bullet (no numbering sentinel); positive numId => numbered.
        WordDocument doc = ReadBody(
            """
            <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="0"/></w:numPr></w:pPr><w:r><w:t>Bullet A</w:t></w:r></w:p>
            <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="0"/></w:numPr></w:pPr><w:r><w:t>Bullet B</w:t></w:r></w:p>
            <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="3"/></w:numPr></w:pPr><w:r><w:t>Step 1</w:t></w:r></w:p>
            <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="3"/></w:numPr></w:pPr><w:r><w:t>Step 2</w:t></w:r></w:p>
            """);

        Assert.Equal(2, doc.Blocks.Count);

        WordList bullets = Assert.IsType<WordList>(doc.Blocks[0]);
        Assert.False(bullets.Ordered);
        Assert.Equal(2, bullets.Items.Count);
        Assert.Equal("Bullet A", PlainText(bullets.Items[0]));
        Assert.Equal("Bullet B", PlainText(bullets.Items[1]));

        WordList numbered = Assert.IsType<WordList>(doc.Blocks[1]);
        Assert.True(numbered.Ordered);
        Assert.Equal(["Step 1", "Step 2"], numbered.Items.Select(PlainText).ToArray());
    }

    [Fact]
    public void Reads_a_2x2_table_with_a_header_row()
    {
        WordDocument doc = ReadBody(
            """
            <w:tbl>
              <w:tr>
                <w:tc><w:p><w:r><w:t>Name</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Role</w:t></w:r></w:p></w:tc>
              </w:tr>
              <w:tr>
                <w:tc><w:p><w:r><w:t>Ada</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Analyst</w:t></w:r></w:p></w:tc>
              </w:tr>
            </w:tbl>
            """);

        WordTable table = Assert.IsType<WordTable>(Assert.Single(doc.Blocks));
        Assert.Equal(2, table.Rows.Count);

        Assert.Equal(["Name", "Role"], table.Rows[0].Cells.Select(c => PlainText(c.Runs)).ToArray());
        Assert.Equal(["Ada", "Analyst"], table.Rows[1].Cells.Select(c => PlainText(c.Runs)).ToArray());
    }

    [Fact]
    public void Unstyled_paragraph_with_no_runs_is_an_empty_paragraph()
    {
        WordDocument doc = ReadBody("<w:p/>");
        WordParagraph para = Assert.IsType<WordParagraph>(Assert.Single(doc.Blocks));
        Assert.Empty(para.Runs);
    }

    [Fact]
    public void Missing_document_part_yields_an_empty_document()
    {
        byte[] bytes = BuildPackage(documentXml: null, stylesXml: null);
        WordDocument doc = DocxReader.Read(bytes);
        Assert.Empty(doc.Blocks);
    }

    private static string PlainText(IReadOnlyList<WordRun> runs) =>
        string.Concat(runs.Select(r => r.Text));

    private static WordDocument ReadBody(string bodyXml, string? stylesXml = null)
    {
        string document =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"" + WordprocessingMl + "\"><w:body>" +
            bodyXml +
            "</w:body></w:document>";

        string? styles = stylesXml is null
            ? null
            : "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
              "<w:styles xmlns:w=\"" + WordprocessingMl + "\">" + stylesXml + "</w:styles>";

        return DocxReader.Read(BuildPackage(document, styles));
    }

    // Builds a minimal .docx ZIP package: [Content_Types].xml, the package rels, and
    // the word/document.xml (+ optional word/styles.xml) parts.
    private static byte[] BuildPackage(string? documentXml, string? stylesXml)
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            StringBuilder types = new();
            types.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            types.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            types.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            types.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            if (documentXml is not null)
            {
                types.Append("<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>");
            }

            if (stylesXml is not null)
            {
                types.Append("<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>");
            }

            types.Append("</Types>");
            AddEntry(zip, "[Content_Types].xml", types.ToString());

            AddEntry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "</Relationships>");

            if (documentXml is not null)
            {
                AddEntry(zip, "word/document.xml", documentXml);
            }

            if (stylesXml is not null)
            {
                AddEntry(zip, "word/styles.xml", stylesXml);
            }
        }

        return stream.ToArray();
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
