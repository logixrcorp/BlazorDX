using System.IO.Compression;
using System.Text;
using BlazorDX.Primitives.Grid;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Verifies the hand-rolled OOXML writer produces a structurally valid .xlsx
/// package: the required parts are present, the worksheet XML carries the cells,
/// numbers vs. text are distinguished, and special characters are escaped.
/// </summary>
public sealed class XlsxWriterTests
{
    private static Dictionary<string, string> ReadParts(byte[] bytes)
    {
        using MemoryStream stream = new(bytes);
        using ZipArchive zip = new(stream, ZipArchiveMode.Read);
        Dictionary<string, string> parts = new(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            using StreamReader reader = new(entry.Open(), Encoding.UTF8);
            parts[entry.FullName] = reader.ReadToEnd();
        }

        return parts;
    }

    private static string Sheet(byte[] bytes) => ReadParts(bytes)["xl/worksheets/sheet1.xml"];

    [Fact]
    public void Package_contains_the_required_ooxml_parts()
    {
        byte[] bytes = XlsxWriter.Write(["A"], [new[] { "1" }]);

        Dictionary<string, string> parts = ReadParts(bytes);

        Assert.Contains("[Content_Types].xml", parts.Keys);
        Assert.Contains("_rels/.rels", parts.Keys);
        Assert.Contains("xl/workbook.xml", parts.Keys);
        Assert.Contains("xl/_rels/workbook.xml.rels", parts.Keys);
        Assert.Contains("xl/styles.xml", parts.Keys);
        Assert.Contains("xl/worksheets/sheet1.xml", parts.Keys);
    }

    [Fact]
    public void Header_row_is_bold_and_uses_inline_strings()
    {
        string sheet = Sheet(XlsxWriter.Write(["Name", "Qty"], []));

        // Both header cells carry the bold style index and inline-string text.
        Assert.Contains("<c r=\"A1\" s=\"1\" t=\"inlineStr\"><is><t xml:space=\"preserve\">Name</t></is></c>", sheet);
        Assert.Contains("<c r=\"B1\" s=\"1\" t=\"inlineStr\"><is><t xml:space=\"preserve\">Qty</t></is></c>", sheet);
        Assert.Contains("<dimension ref=\"A1:B1\"/>", sheet);
    }

    [Fact]
    public void Numeric_values_become_number_cells_but_identifiers_stay_text()
    {
        string sheet = Sheet(XlsxWriter.Write(["N"], [new[] { "10" }, new[] { "007" }, new[] { "3.5" }]));

        // Clean numbers -> numeric <v> cells (no t attribute).
        Assert.Contains("<c r=\"A2\"><v>10</v></c>", sheet);
        Assert.Contains("<c r=\"A4\"><v>3.5</v></c>", sheet);
        // "007" would lose its leading zeros as a number, so it stays an inline string.
        Assert.Contains("<c r=\"A3\" t=\"inlineStr\"><is><t xml:space=\"preserve\">007</t></is></c>", sheet);
    }

    [Fact]
    public void Special_characters_are_xml_escaped()
    {
        string sheet = Sheet(XlsxWriter.Write(["X"], [new[] { "a<b>&\"c\"" }]));

        Assert.Contains("a&lt;b&gt;&amp;\"c\"", sheet);
        Assert.DoesNotContain("<b>", sheet.Replace("&lt;b&gt;", string.Empty));
    }

    [Fact]
    public void Columns_past_z_use_two_letter_references()
    {
        // 27 single-character headers -> the 27th cell is column "AA".
        string[] headers = Enumerable.Range(0, 27).Select(_ => "h").ToArray();
        string sheet = Sheet(XlsxWriter.Write(headers, []));

        Assert.Contains("<c r=\"AA1\"", sheet);
    }
}
