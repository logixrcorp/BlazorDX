using System.Linq;
using BlazorDX.Documents;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Round-trip coverage for the Word model's character/paragraph formatting through both
/// serializers: <c>WordHtml.ToHtml/FromHtml</c> and <c>DocxWriter.Write</c>/<c>DocxReader.Read</c>.
/// </summary>
public sealed class DxWordRoundTripTests
{
    private static string Text(IEnumerable<WordRun> runs) => string.Concat(runs.Select(r => r.Text));

    [Fact]
    public void Typography_round_trips_through_html()
    {
        WordDocument doc = new(
        [
            new WordParagraph(
            [
                new WordRun("E=mc", FontFamily: "Arial", FontSizePoints: 14),
                new WordRun("2", VerticalAlign: WordVerticalAlign.Superscript),
            ]),
        ]);

        WordParagraph para = WordHtml.FromHtml(WordHtml.ToHtml(doc)).Blocks.OfType<WordParagraph>().Single();

        Assert.Equal("Arial", para.Runs[0].FontFamily);
        Assert.Equal(14d, para.Runs[0].FontSizePoints);
        Assert.Equal(WordVerticalAlign.Superscript, para.Runs[^1].VerticalAlign);
        Assert.Equal("E=mc2", Text(para.Runs));
    }

    [Fact]
    public void Paragraph_spacing_and_indent_round_trip_through_html_and_docx()
    {
        WordDocument doc = new(
            [new WordParagraph([new WordRun("Body")], WordAlignment.Start, LineSpacing: 1.5, IndentLevel: 2)]);

        WordParagraph viaHtml = WordHtml.FromHtml(WordHtml.ToHtml(doc)).Blocks.OfType<WordParagraph>().Single();
        Assert.Equal(1.5, viaHtml.LineSpacing);
        Assert.Equal(2, viaHtml.IndentLevel);

        WordParagraph viaDocx = DocxReader.Read(DocxWriter.Write(doc)).Blocks.OfType<WordParagraph>().Single();
        Assert.Equal(1.5, viaDocx.LineSpacing);
        Assert.Equal(2, viaDocx.IndentLevel);
    }
}
