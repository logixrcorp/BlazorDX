using BlazorDX.Documents.Formula;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Exercises the incremental recalc engine: cell-for-cell parity with the whole-sheet
/// <see cref="WorkbookRecalc"/>, dirty-set propagation after an edit, dependency-graph
/// updates when a formula's references change, and circular-reference handling on edit.
/// </summary>
public sealed class IncrementalWorkbookTests
{
    private static string Display(CellValue v) => v.ToDisplayString();

    [Fact]
    public void Initial_build_matches_whole_sheet_recalc_cell_for_cell()
    {
        string[][] sheet =
        [
            ["1", "=A1+10", "=B1*2", "=SUM(A1:C1)"],
            ["2", "=A2+B1", "=C1+B2", "=AVERAGE(A1:A2)"],
            ["=B2", "text", "=NOPE(", "=A1/0"], // forward ref, literal, parse error, div0
        ];

        CellValue[][] expected = WorkbookRecalc.Recalculate(sheet);
        CellValue[][] actual = new IncrementalWorkbook(sheet).ToValues();

        for (int r = 0; r < expected.Length; r++)
        {
            for (int c = 0; c < expected[r].Length; c++)
            {
                Assert.True(expected[r][c].Equals(actual[r][c]),
                    $"cell ({r},{c}): expected {Display(expected[r][c])}, got {Display(actual[r][c])}");
            }
        }
    }

    [Fact]
    public void Editing_a_cell_propagates_to_transitive_dependents_only()
    {
        // A1=1 -> B1==A1+1 -> C1==B1*2 ; D1 is independent.
        var wb = new IncrementalWorkbook([["1", "=A1+1", "=B1*2", "=99"]]);
        Assert.Equal(2d, wb.GetValue(0, 1).AsRawNumber);
        Assert.Equal(4d, wb.GetValue(0, 2).AsRawNumber);

        wb.SetCell(0, 0, "10"); // edit A1

        Assert.Equal(10d, wb.GetValue(0, 0).AsRawNumber);
        Assert.Equal(11d, wb.GetValue(0, 1).AsRawNumber); // B1 recomputed
        Assert.Equal(22d, wb.GetValue(0, 2).AsRawNumber); // C1 recomputed transitively
        Assert.Equal(99d, wb.GetValue(0, 3).AsRawNumber); // D1 untouched
    }

    [Fact]
    public void Changing_a_formulas_references_rewires_the_dependency_graph()
    {
        // B1 starts depending on A1, then is re-pointed at C1.
        var wb = new IncrementalWorkbook([["1", "=A1", "5"]]);
        Assert.Equal(1d, wb.GetValue(0, 1).AsRawNumber);

        wb.SetCell(0, 1, "=C1"); // B1 now reads C1
        Assert.Equal(5d, wb.GetValue(0, 1).AsRawNumber);

        wb.SetCell(0, 0, "100"); // A1 no longer feeds B1
        Assert.Equal(5d, wb.GetValue(0, 1).AsRawNumber);

        wb.SetCell(0, 2, "7"); // C1 now does
        Assert.Equal(7d, wb.GetValue(0, 1).AsRawNumber);
    }

    [Fact]
    public void Editing_into_and_out_of_a_cycle_flags_then_clears_circular()
    {
        // A1==B1, B1=2  ->  A1=2.
        var wb = new IncrementalWorkbook([["=B1", "2"]]);
        Assert.Equal(2d, wb.GetValue(0, 0).AsRawNumber);

        // Make B1 point back at A1 -> mutual cycle, both #CIRC!.
        wb.SetCell(0, 1, "=A1");
        Assert.Equal(FormulaError.Circular, wb.GetValue(0, 0).ErrorValue);
        Assert.Equal(FormulaError.Circular, wb.GetValue(0, 1).ErrorValue);

        // Break the cycle -> both recover.
        wb.SetCell(0, 1, "3");
        Assert.Equal(3d, wb.GetValue(0, 1).AsRawNumber);
        Assert.Equal(3d, wb.GetValue(0, 0).AsRawNumber);
    }

    [Fact]
    public void Edits_stay_in_parity_with_a_full_recalc_of_the_same_text()
    {
        string[][] sheet =
        [
            ["1", "=A1+1", "=B1+A1"],
            ["=A1*2", "=SUM(A1:C1)", "=B2-1"],
        ];
        var wb = new IncrementalWorkbook(sheet);

        // Apply a couple of edits to the live workbook...
        wb.SetCell(0, 0, "=B2+1");
        wb.SetCell(1, 2, "=A1+B1");

        // ...and to the raw text, then full-recalc and compare.
        sheet[0][0] = "=B2+1";
        sheet[1][2] = "=A1+B1";
        CellValue[][] expected = WorkbookRecalc.Recalculate(sheet);
        CellValue[][] actual = wb.ToValues();

        for (int r = 0; r < expected.Length; r++)
        {
            for (int c = 0; c < expected[r].Length; c++)
            {
                Assert.True(expected[r][c].Equals(actual[r][c]),
                    $"cell ({r},{c}): expected {Display(expected[r][c])}, got {Display(actual[r][c])}");
            }
        }
    }
}
