using System.IO.Compression;
using System.Text;

namespace BlazorDX.Demo;

/// <summary>
/// Builds deterministic sample <c>.xlsx</c> / <c>.docx</c> bytes for the static-SSR
/// HTMX document-viewer demo. The bytes are rebuilt identically on every request, so
/// the sheet-switch / paging endpoint can re-parse them without any session state — the
/// no-JS full-page path and the HTMX fragment path hit the exact same document.
/// </summary>
/// <remarks>
/// These are hand-assembled minimal OOXML packages (a few XML parts in a ZIP), the same
/// shape <see cref="System.IO.Compression.ZipArchive"/> + <c>XmlReader</c> the parsers
/// consume. Kept in the demo (not the library) because they exist only to feed the demo.
/// </remarks>
public static class SampleDocuments
{
    private const string SpreadsheetMl =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string WordprocessingMl =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string Rels =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>A two-sheet workbook: a 60-row "Ledger" plus a short "Summary".</summary>
    public static byte[] Workbook()
    {
        string[] accounts = ["Cash", "Revenue", "Supplies", "Payroll", "Rent"];
        string[] notes = ["Invoice", "Refund", "Restock", "Salary run", "Monthly lease"];

        StringBuilder ledger = new();
        ledger.Append(Row(1, ["Date", "Account", "Description", "Debit", "Credit"]));
        for (int i = 0; i < 60; i++)
        {
            ledger.Append(Row(i + 2,
            [
                new DateOnly(2026, 1, 1).AddDays(i).ToString("yyyy-MM-dd"),
                accounts[i % accounts.Length],
                $"{notes[i % notes.Length]} #{i + 1}",
                ((i % 7) * 125).ToString(),
                ((i % 5) * 90).ToString(),
            ]));
        }

        string summary =
            Row(1, ["Metric", "Value"]) +
            Row(2, ["Entries", "60"]) +
            Row(3, ["Accounts", "5"]) +
            Row(4, ["Period", "2026"]);

        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "</Types>");
            Add(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                $"<Relationship Id=\"rId1\" Type=\"{Rels}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                $"<workbook xmlns=\"{SpreadsheetMl}\" xmlns:r=\"{Rels}\"><sheets>" +
                "<sheet name=\"Ledger\" sheetId=\"1\" r:id=\"rId1\"/>" +
                "<sheet name=\"Summary\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                $"<Relationship Id=\"rId1\" Type=\"{Rels}/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                $"<Relationship Id=\"rId2\" Type=\"{Rels}/worksheet\" Target=\"worksheets/sheet2.xml\"/></Relationships>");
            Add(zip, "xl/worksheets/sheet1.xml", Sheet(ledger.ToString()));
            Add(zip, "xl/worksheets/sheet2.xml", Sheet(summary));
        }

        return stream.ToArray();
    }

    /// <summary>A short document with a heading outline, emphasis, a list, and a table.</summary>
    public static byte[] WordDocument()
    {
        StringBuilder body = new();
        body.Append(Heading(1, "Quarterly Report"));
        body.Append(Heading(2, "Overview"));
        body.Append("<w:p><w:r><w:t xml:space=\"preserve\">This quarter was </w:t></w:r>" +
            "<w:r><w:rPr><w:b/></w:rPr><w:t>strong</w:t></w:r>" +
            "<w:r><w:t xml:space=\"preserve\"> and </w:t></w:r>" +
            "<w:r><w:rPr><w:i/></w:rPr><w:t>steady</w:t></w:r>" +
            "<w:r><w:t>.</w:t></w:r></w:p>");
        body.Append(Heading(2, "Highlights"));
        body.Append(ListItem("Revenue grew across every region."));
        body.Append(ListItem("Operating costs held flat."));
        body.Append(ListItem("Headcount stayed steady."));
        body.Append(Heading(2, "Numbers"));
        body.Append("<w:tbl>" +
            TableRow("Metric", "Value") +
            TableRow("Revenue", "1,240,000") +
            TableRow("Costs", "880,000") +
            "</w:tbl>");
        body.Append(Para("Full figures are available in the accompanying workbook."));

        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "</Types>");
            Add(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                $"<Relationship Id=\"rId1\" Type=\"{Rels}/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(zip, "word/document.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                $"<w:document xmlns:w=\"{WordprocessingMl}\"><w:body>{body}</w:body></w:document>");
        }

        return stream.ToArray();
    }

    private static string Sheet(string rows) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<worksheet xmlns=\"{SpreadsheetMl}\"><sheetData>{rows}</sheetData></worksheet>";

    private static string Row(int rowNumber, string[] cells)
    {
        StringBuilder sb = new();
        sb.Append($"<row r=\"{rowNumber}\">");
        for (int c = 0; c < cells.Length; c++)
        {
            // Inline strings keep the sample self-contained (no shared-string table).
            sb.Append($"<c r=\"{Column(c)}{rowNumber}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
            sb.Append(Encode(cells[c]));
            sb.Append("</t></is></c>");
        }

        sb.Append("</row>");
        return sb.ToString();
    }

    private static string Column(int index)
    {
        // 0 -> A, 25 -> Z, 26 -> AA. The sample never exceeds a handful of columns.
        Span<char> buffer = stackalloc char[4];
        int i = buffer.Length;
        int n = index;
        do
        {
            buffer[--i] = (char)('A' + (n % 26));
            n = (n / 26) - 1;
        }
        while (n >= 0);

        return new string(buffer[i..]);
    }

    private static string Heading(int level, string text) =>
        $"<w:p><w:pPr><w:pStyle w:val=\"Heading{level}\"/></w:pPr>" +
        $"<w:r><w:t xml:space=\"preserve\">{Encode(text)}</w:t></w:r></w:p>";

    private static string Para(string text) =>
        $"<w:p><w:r><w:t xml:space=\"preserve\">{Encode(text)}</w:t></w:r></w:p>";

    private static string ListItem(string text) =>
        "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"0\"/></w:numPr></w:pPr>" +
        $"<w:r><w:t xml:space=\"preserve\">{Encode(text)}</w:t></w:r></w:p>";

    private static string TableRow(string a, string b) =>
        "<w:tr>" +
        $"<w:tc><w:p><w:r><w:t xml:space=\"preserve\">{Encode(a)}</w:t></w:r></w:p></w:tc>" +
        $"<w:tc><w:p><w:r><w:t xml:space=\"preserve\">{Encode(b)}</w:t></w:r></w:p></w:tc>" +
        "</w:tr>";

    private static string Encode(string s) => System.Security.SecurityElement.Escape(s) ?? s;

    private static void Add(ZipArchive zip, string path, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
