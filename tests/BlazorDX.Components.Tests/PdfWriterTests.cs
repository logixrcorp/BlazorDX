using System.Text;
using System.Text.RegularExpressions;
using BlazorDX.Primitives.Grid;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Verifies the hand-rolled PDF writer emits a structurally valid PDF 1.4 file:
/// the header/trailer markers, catalog and font objects, the cell text in the
/// content stream, parenthesis escaping, and multi-page pagination.
/// </summary>
public sealed class PdfWriterTests
{
    // Content streams are uncompressed, so the whole file reads cleanly as Latin1.
    private static string AsText(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [Fact]
    public void Output_has_the_pdf_header_and_trailer()
    {
        string pdf = AsText(PdfWriter.Write(["A"], [new[] { "1" }]));

        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.Contains("trailer", pdf);
        Assert.Contains("/Root 1 0 R", pdf);
        Assert.Contains("startxref", pdf);
        Assert.EndsWith("%%EOF", pdf);
    }

    [Fact]
    public void Output_declares_a_catalog_and_both_helvetica_fonts()
    {
        string pdf = AsText(PdfWriter.Write(["A"], []));

        Assert.Contains("/Type /Catalog", pdf);
        Assert.Contains("/BaseFont /Helvetica ", pdf);
        Assert.Contains("/BaseFont /Helvetica-Bold", pdf);
    }

    [Fact]
    public void Cell_and_header_text_appear_in_the_content_stream()
    {
        string pdf = AsText(PdfWriter.Write(["Name", "Qty"], [new[] { "Alpha", "10" }]));

        Assert.Contains("(Name) Tj", pdf);
        Assert.Contains("(Qty) Tj", pdf);
        Assert.Contains("(Alpha) Tj", pdf);
        Assert.Contains("(10) Tj", pdf);
    }

    [Fact]
    public void Parentheses_and_backslashes_are_escaped()
    {
        string pdf = AsText(PdfWriter.Write(["X"], [new[] { "a(b)c" }]));

        Assert.Contains(@"(a\(b\)c) Tj", pdf);
    }

    [Fact]
    public void Many_rows_paginate_into_multiple_pages()
    {
        List<IReadOnlyList<string>> rows = Enumerable.Range(0, 100)
            .Select(i => (IReadOnlyList<string>)new[] { "row" + i })
            .ToList();

        string pdf = AsText(PdfWriter.Write(["H"], rows));

        // 100 rows at ~43 data rows/page -> 3 pages.
        Assert.Contains("/Count 3", pdf);
        Assert.Equal(3, Regex.Matches(pdf, @"/Type /Page /Parent").Count);
    }
}
