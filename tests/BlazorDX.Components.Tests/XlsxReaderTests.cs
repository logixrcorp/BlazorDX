using System.IO.Compression;
using System.Text;
using BlazorDX.Documents;
using BlazorDX.Primitives.Grid;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The C# .xlsx reader: round-trips what <see cref="XlsxWriter"/> produces, and parses
/// the parts a real workbook adds (shared strings, numeric/inline/formula-string cells,
/// ragged rows, multiple sheets, an empty sheet). Multi-part fixtures are hand-built as
/// minimal OOXML packages so the test owns exactly what the reader sees.
/// </summary>
public sealed class XlsxReaderTests
{
    private const string SpreadsheetMl =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Round_trips_writer_output_sheet_name_and_cell_values()
    {
        string[] headers = ["Name", "Age", "City"];
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["Alice", "30", "Seattle"],
            ["Bob", "25", "Austin"],
        ];

        byte[] bytes = XlsxWriter.Write(headers, rows);
        Workbook workbook = XlsxReader.Read(bytes);

        Worksheet sheet = Assert.Single(workbook.Sheets);
        Assert.Equal("Sheet1", sheet.Name);
        Assert.Equal(3, sheet.ColumnCount);
        Assert.Equal(3, sheet.Rows.Count); // header + 2 data rows

        Assert.Equal(["Name", "Age", "City"], sheet.Rows[0]);
        Assert.Equal(["Alice", "30", "Seattle"], sheet.Rows[1]);
        Assert.Equal(["Bob", "25", "Austin"], sheet.Rows[2]);
    }

    [Fact]
    public void Numeric_cells_keep_their_invariant_text()
    {
        // The writer emits numeric <v> cells for values that round-trip; "007" stays text.
        byte[] bytes = XlsxWriter.Write(["Code", "Qty"], [["007", "42"]]);
        Workbook workbook = XlsxReader.Read(bytes);

        Worksheet sheet = Assert.Single(workbook.Sheets);
        Assert.Equal("007", sheet.Rows[1][0]); // preserved as text (not "7")
        Assert.Equal("42", sheet.Rows[1][1]);  // numeric <v>
    }

    [Fact]
    public void Resolves_shared_strings_by_index()
    {
        byte[] bytes = BuildWorkbook(
            sharedStrings: ["Apple", "Banana"],
            sheets:
            [
                ("Fruit",
                    """
                    <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
                    <row r="2"><c r="A2" t="s"><v>1</v></c><c r="B2" t="s"><v>0</v></c></row>
                    """),
            ]);

        Workbook workbook = XlsxReader.Read(bytes);
        Worksheet sheet = Assert.Single(workbook.Sheets);

        Assert.Equal(["Apple", "Banana"], sheet.Rows[0]);
        Assert.Equal(["Banana", "Apple"], sheet.Rows[1]);
    }

    [Fact]
    public void Handles_inline_and_formula_string_and_numeric_cells()
    {
        byte[] bytes = BuildWorkbook(
            sharedStrings: [],
            sheets:
            [
                ("Mix",
                    """
                    <row r="1">
                      <c r="A1" t="inlineStr"><is><t>Inline</t></is></c>
                      <c r="B1" t="str"><f>A1</f><v>Formula</v></c>
                      <c r="C1"><v>3.5</v></c>
                    </row>
                    """),
            ]);

        Workbook workbook = XlsxReader.Read(bytes);
        Worksheet sheet = Assert.Single(workbook.Sheets);

        // A formula cell round-trips as its source ("=A1"), not the cached <v>;
        // the inline string and numeric value cells are unchanged.
        Assert.Equal(["Inline", "=A1", "3.5"], sheet.Rows[0]);
    }

    [Fact]
    public void Pads_ragged_rows_and_preserves_empty_cells()
    {
        // Row 1 has 3 cells; row 2 has a gap (B omitted) then C; row 3 has 1 cell.
        byte[] bytes = BuildWorkbook(
            sharedStrings: [],
            sheets:
            [
                ("Ragged",
                    """
                    <row r="1"><c r="A1" t="inlineStr"><is><t>a</t></is></c><c r="B1" t="inlineStr"><is><t>b</t></is></c><c r="C1" t="inlineStr"><is><t>c</t></is></c></row>
                    <row r="2"><c r="A2" t="inlineStr"><is><t>d</t></is></c><c r="C2" t="inlineStr"><is><t>f</t></is></c></row>
                    <row r="3"><c r="A3" t="inlineStr"><is><t>g</t></is></c></row>
                    """),
            ]);

        Workbook workbook = XlsxReader.Read(bytes);
        Worksheet sheet = Assert.Single(workbook.Sheets);

        Assert.Equal(3, sheet.ColumnCount);
        Assert.Equal(["a", "b", "c"], sheet.Rows[0]);
        Assert.Equal(["d", "", "f"], sheet.Rows[1]); // gap at B preserved as ""
        Assert.Equal(["g", "", ""], sheet.Rows[2]);  // padded to width 3
    }

    [Fact]
    public void Reads_multiple_sheets_in_workbook_order()
    {
        byte[] bytes = BuildWorkbook(
            sharedStrings: [],
            sheets:
            [
                ("First", """<row r="1"><c r="A1" t="inlineStr"><is><t>one</t></is></c></row>"""),
                ("Second", """<row r="1"><c r="A1" t="inlineStr"><is><t>two</t></is></c></row>"""),
                ("Third", """<row r="1"><c r="A1" t="inlineStr"><is><t>three</t></is></c></row>"""),
            ]);

        Workbook workbook = XlsxReader.Read(bytes);

        Assert.Equal(3, workbook.Sheets.Count);
        Assert.Equal(["First", "Second", "Third"], workbook.Sheets.Select(s => s.Name).ToArray());
        Assert.Equal("one", workbook.Sheets[0].Rows[0][0]);
        Assert.Equal("two", workbook.Sheets[1].Rows[0][0]);
        Assert.Equal("three", workbook.Sheets[2].Rows[0][0]);
    }

    [Fact]
    public void Reads_an_empty_sheet_as_zero_rows()
    {
        byte[] bytes = BuildWorkbook(
            sharedStrings: [],
            sheets:
            [
                ("Data", """<row r="1"><c r="A1" t="inlineStr"><is><t>x</t></is></c></row>"""),
                ("Blank", string.Empty), // no <row> children
            ]);

        Workbook workbook = XlsxReader.Read(bytes);

        Assert.Equal(2, workbook.Sheets.Count);
        Worksheet blank = workbook.Sheets[1];
        Assert.Equal("Blank", blank.Name);
        Assert.Empty(blank.Rows);
        Assert.Equal(0, blank.ColumnCount);
    }

    // Builds a minimal but valid .xlsx package by hand (same ZIP/OOXML shape the writer
    // uses), so tests can exercise multi-sheet and shared-string paths the single-sheet
    // writer does not produce.
    private static byte[] BuildWorkbook(
        IReadOnlyList<string> sharedStrings,
        IReadOnlyList<(string Name, string SheetData)> sheets)
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            StringBuilder types = new();
            types.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            types.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            types.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            types.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            types.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            for (int i = 0; i < sheets.Count; i++)
            {
                types.Append("<Override PartName=\"/xl/worksheets/sheet").Append(i + 1)
                     .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }

            if (sharedStrings.Count > 0)
            {
                types.Append("<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>");
            }

            types.Append("</Types>");
            AddEntry(zip, "[Content_Types].xml", types.ToString());

            AddEntry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");

            // workbook.xml — sheets in order, each linked by r:id.
            StringBuilder wb = new();
            wb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            wb.Append("<workbook xmlns=\"").Append(SpreadsheetMl)
              .Append("\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            for (int i = 0; i < sheets.Count; i++)
            {
                wb.Append("<sheet name=\"").Append(sheets[i].Name).Append("\" sheetId=\"").Append(i + 1)
                  .Append("\" r:id=\"rId").Append(i + 1).Append("\"/>");
            }

            wb.Append("</sheets></workbook>");
            AddEntry(zip, "xl/workbook.xml", wb.ToString());

            // workbook.xml.rels — r:id -> worksheet part (+ sharedStrings if present).
            StringBuilder rels = new();
            rels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            rels.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 0; i < sheets.Count; i++)
            {
                rels.Append("<Relationship Id=\"rId").Append(i + 1)
                    .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet")
                    .Append(i + 1).Append(".xml\"/>");
            }

            if (sharedStrings.Count > 0)
            {
                rels.Append("<Relationship Id=\"rIdSst\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>");
            }

            rels.Append("</Relationships>");
            AddEntry(zip, "xl/_rels/workbook.xml.rels", rels.ToString());

            if (sharedStrings.Count > 0)
            {
                StringBuilder sst = new();
                sst.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                sst.Append("<sst xmlns=\"").Append(SpreadsheetMl).Append("\" count=\"").Append(sharedStrings.Count)
                   .Append("\" uniqueCount=\"").Append(sharedStrings.Count).Append("\">");
                foreach (string s in sharedStrings)
                {
                    sst.Append("<si><t>").Append(s).Append("</t></si>");
                }

                sst.Append("</sst>");
                AddEntry(zip, "xl/sharedStrings.xml", sst.ToString());
            }

            for (int i = 0; i < sheets.Count; i++)
            {
                string ws =
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<worksheet xmlns=\"" + SpreadsheetMl + "\"><sheetData>" +
                    sheets[i].SheetData +
                    "</sheetData></worksheet>";
                AddEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", ws);
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
