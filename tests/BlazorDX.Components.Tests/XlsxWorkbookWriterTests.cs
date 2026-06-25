using BlazorDX.Documents;
using BlazorDX.Documents.Formula;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The spreadsheet write-back round-trip: an edited <see cref="Workbook"/> of raw
/// content (literals + <c>=</c> formulas) written by <see cref="XlsxWorkbookWriter"/>
/// and read back by <see cref="XlsxReader"/> must preserve the formulas (not just their
/// cached values), so a save survives a reload. This is the real, tested round-trip the
/// editable spreadsheet relies on.
/// </summary>
public sealed class XlsxWorkbookWriterTests
{
    [Fact]
    public void Round_trips_a_formula_cell_as_its_source()
    {
        // C1 = A1 + B1; A1=2, B1=3.
        Workbook workbook = new(
        [
            new Worksheet("Sheet1",
            [
                ["2", "3", "=A1+B1"],
            ], 3),
        ]);

        byte[] bytes = XlsxWorkbookWriter.Write(workbook);
        Workbook reloaded = XlsxReader.Read(bytes);

        Worksheet sheet = Assert.Single(reloaded.Sheets);
        Assert.Equal("Sheet1", sheet.Name);
        // The formula survives as its source text, not the cached value.
        Assert.Equal("=A1+B1", sheet.Rows[0][2]);
        Assert.Equal("2", sheet.Rows[0][0]);
        Assert.Equal("3", sheet.Rows[0][1]);
    }

    [Fact]
    public void Reloaded_formula_recomputes_to_the_same_value()
    {
        Workbook workbook = new(
        [
            new Worksheet("Calc",
            [
                ["Qty", "Price", "Total"],
                ["4", "2.5", "=A2*B2"],
                ["Sum", "", "=SUM(C2:C2)"],
            ], 3),
        ]);

        byte[] bytes = XlsxWorkbookWriter.Write(workbook);
        Workbook reloaded = XlsxReader.Read(bytes);

        // Recomputing the reloaded sheet yields the original result (10).
        CellValue[][] computed = FormulaEngine.Recalculate(reloaded.Sheets[0]);
        Assert.Equal("10", computed[1][2].ToDisplayString());
        Assert.Equal("=A2*B2", reloaded.Sheets[0].Rows[1][2]);
        Assert.Equal("=SUM(C2:C2)", reloaded.Sheets[0].Rows[2][2]);
    }

    [Fact]
    public void Persists_an_error_formula_so_it_round_trips()
    {
        Workbook workbook = new(
        [
            new Worksheet("Err",
            [
                ["=1/0"],
            ], 1),
        ]);

        byte[] bytes = XlsxWorkbookWriter.Write(workbook);
        Workbook reloaded = XlsxReader.Read(bytes);

        Assert.Equal("=1/0", reloaded.Sheets[0].Rows[0][0]);
        CellValue[][] computed = FormulaEngine.Recalculate(reloaded.Sheets[0]);
        Assert.Equal("#DIV/0!", computed[0][0].ToDisplayString());
    }

    [Fact]
    public void Literals_round_trip_preserving_text_and_numbers()
    {
        Workbook workbook = new(
        [
            new Worksheet("Mixed",
            [
                ["Code", "Qty", "Label"],
                ["007", "42", "a<b>&c"],
            ], 3),
        ]);

        byte[] bytes = XlsxWorkbookWriter.Write(workbook);
        Workbook reloaded = XlsxReader.Read(bytes);

        Worksheet sheet = reloaded.Sheets[0];
        Assert.Equal("007", sheet.Rows[1][0]); // leading zeros preserved as text
        Assert.Equal("42", sheet.Rows[1][1]);  // numeric cell
        Assert.Equal("a<b>&c", sheet.Rows[1][2]); // XML-escaped then decoded back
    }

    [Fact]
    public void Writes_multiple_sheets_in_order()
    {
        Workbook workbook = new(
        [
            new Worksheet("First", [["=1+1"]], 1),
            new Worksheet("Second", [["hello"]], 1),
        ]);

        byte[] bytes = XlsxWorkbookWriter.Write(workbook);
        Workbook reloaded = XlsxReader.Read(bytes);

        Assert.Equal(2, reloaded.Sheets.Count);
        Assert.Equal("First", reloaded.Sheets[0].Name);
        Assert.Equal("Second", reloaded.Sheets[1].Name);
        Assert.Equal("=1+1", reloaded.Sheets[0].Rows[0][0]);
        Assert.Equal("hello", reloaded.Sheets[1].Rows[0][0]);
    }
}
