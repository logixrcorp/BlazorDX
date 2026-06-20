using System.Globalization;
using System.Text;

namespace BlazorDX.Primitives.Grid;

/// <summary>
/// A tiny, dependency-free writer for a tabular PDF document. It emits a valid
/// PDF 1.4 file by hand — catalog, page tree, the two built-in Helvetica fonts
/// (so nothing has to be embedded), and one uncompressed content stream per page
/// — with a byte-accurate cross-reference table. No third-party library, no
/// reflection: AOT- and trim-safe, so it runs unchanged in the browser runtime.
/// </summary>
/// <remarks>
/// Rows are laid out as a simple grid on US-Letter pages, the header repeated in
/// bold at the top of each page; cell text is left-aligned and truncated to its
/// column width. Text uses WinAnsi (ASCII) — characters outside it become '?'.
/// </remarks>
internal static class PdfWriter
{
    private const double PageWidth = 612;    // US Letter, 72 dpi points
    private const double PageHeight = 792;
    private const double Margin = 40;
    private const double FontSize = 9;
    private const double RowHeight = 16;
    private const double Top = PageHeight - Margin;
    private const double Bottom = Margin;

    /// <summary>The IANA media type browsers expect for a .pdf download.</summary>
    public const string MimeType = "application/pdf";

    /// <summary>
    /// Builds a paginated table PDF: <paramref name="headers"/> form a bold,
    /// per-page header row, then each item of <paramref name="rows"/> is a row.
    /// </summary>
    public static byte[] Write(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        int columns = Math.Max(headers.Count, 1);
        double usableWidth = PageWidth - (2 * Margin);
        double columnWidth = usableWidth / columns;
        int rowsPerPage = Math.Max((int)((Top - Bottom) / RowHeight) - 1, 1);   // reserve the header row

        // Split the data rows into pages, then render each page's content stream.
        List<string> pages = new();
        int pageCount = Math.Max((rows.Count + rowsPerPage - 1) / rowsPerPage, 1);
        for (int page = 0; page < pageCount; page++)
        {
            int start = page * rowsPerPage;
            int end = Math.Min(start + rowsPerPage, rows.Count);
            pages.Add(BuildPageContent(headers, rows, start, end, columnWidth, columns));
        }

        return Assemble(pages);
    }

    private static string BuildPageContent(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        int start,
        int end,
        double columnWidth,
        int columns)
    {
        StringBuilder cs = new();
        double y = Top - RowHeight;

        DrawRow(cs, headers, columns, y, columnWidth, bold: true);
        // A rule under the header row.
        cs.Append("0.5 w 0.6 G ").Append(F(Margin)).Append(' ').Append(F(y - 3)).Append(" m ")
          .Append(F(PageWidth - Margin)).Append(' ').Append(F(y - 3)).Append(" l S\n");
        y -= RowHeight;

        for (int r = start; r < end; r++)
        {
            DrawRow(cs, rows[r], columns, y, columnWidth, bold: false);
            y -= RowHeight;
        }

        return cs.ToString();
    }

    private static void DrawRow(
        StringBuilder cs,
        IReadOnlyList<string> cells,
        int columns,
        double y,
        double columnWidth,
        bool bold)
    {
        string font = bold ? "/F2" : "/F1";
        for (int c = 0; c < columns; c++)
        {
            string raw = c < cells.Count ? cells[c] ?? string.Empty : string.Empty;
            string text = EscapePdf(Truncate(raw, columnWidth));
            double x = Margin + (c * columnWidth) + 2;
            cs.Append("BT 0 g ").Append(font).Append(' ').Append(F(FontSize)).Append(" Tf ")
              .Append(F(x)).Append(' ').Append(F(y)).Append(" Td (").Append(text).Append(") Tj ET\n");
        }
    }

    // Helvetica averages ~0.5 em per character; truncate (with an ellipsis "...")
    // so text never bleeds past its column.
    private static string Truncate(string text, double columnWidth)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        int maxChars = Math.Max((int)((columnWidth - 4) / (FontSize * 0.5)), 1);
        if (text.Length <= maxChars)
        {
            return text;
        }

        return maxChars <= 3 ? text[..maxChars] : text[..(maxChars - 3)] + "...";
    }

    private static byte[] Assemble(List<string> pageContents)
    {
        int pageCount = pageContents.Count;
        // Object numbering: 1 catalog, 2 pages, 3 F1, 4 F2,
        // then page objects [5 .. 4+P], then content streams [5+P .. 4+2P].
        int firstPage = 5;
        int firstContent = 5 + pageCount;
        int objectCount = 4 + (2 * pageCount);

        using MemoryStream stream = new();
        long[] offsets = new long[objectCount + 1];   // 1-based; index 0 is the free entry

        WriteAscii(stream, "%PDF-1.4\n%âãÏÓ\n");

        // 1: Catalog
        offsets[1] = stream.Position;
        WriteAscii(stream, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        // 2: Pages
        StringBuilder kids = new();
        for (int p = 0; p < pageCount; p++)
        {
            kids.Append(firstPage + p).Append(" 0 R ");
        }

        offsets[2] = stream.Position;
        WriteAscii(stream, $"2 0 obj\n<< /Type /Pages /Kids [ {kids.ToString().TrimEnd()} ] /Count {pageCount} >>\nendobj\n");

        // 3 & 4: the two built-in fonts
        offsets[3] = stream.Position;
        WriteAscii(stream, "3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");
        offsets[4] = stream.Position;
        WriteAscii(stream, "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

        // Page objects
        for (int p = 0; p < pageCount; p++)
        {
            int objNum = firstPage + p;
            int contentNum = firstContent + p;
            offsets[objNum] = stream.Position;
            WriteAscii(stream,
                $"{objNum} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(PageWidth)} {F(PageHeight)}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentNum} 0 R >>\nendobj\n");
        }

        // Content stream objects
        for (int p = 0; p < pageCount; p++)
        {
            int objNum = firstContent + p;
            string body = pageContents[p];
            int length = Encoding.Latin1.GetByteCount(body);
            offsets[objNum] = stream.Position;
            WriteAscii(stream, $"{objNum} 0 obj\n<< /Length {length} >>\nstream\n{body}\nendstream\nendobj\n");
        }

        // Cross-reference table
        long xref = stream.Position;
        WriteAscii(stream, $"xref\n0 {objectCount + 1}\n0000000000 65535 f \n");
        for (int i = 1; i <= objectCount; i++)
        {
            WriteAscii(stream, offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        WriteAscii(stream,
            $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");

        return stream.ToArray();
    }

    private static string EscapePdf(string value)
    {
        StringBuilder sb = new(value.Length);
        foreach (char c in value)
        {
            if (c is '\\' or '(' or ')')
            {
                sb.Append('\\').Append(c);
            }
            else if (c is >= ' ' and <= '~')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('?');   // outside printable ASCII; keep the file WinAnsi-safe
            }
        }

        return sb.ToString();
    }

    // PDF numbers: invariant, trimmed, no exponent.
    private static string F(double value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static void WriteAscii(MemoryStream stream, string text)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
