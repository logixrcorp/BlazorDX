using System.Linq;
using BlazorDX.Documents;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Model-driven table-structure commands on <see cref="DxWordEditor"/>: horizontal cell merge
/// and split, resolved through the table-cell selection bridge.
/// </summary>
public sealed class DxWordTableTests : TestContext
{
    public DxWordTableTests()
    {
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    private static string Text(IEnumerable<WordRun> runs) => string.Concat(runs.Select(r => r.Text));

    [Fact]
    public void Merge_cell_right_spans_two_columns_and_merges_content()
    {
        FakeRichTextInterop fake = new() { TableCell = "0,0,0" }; // table 0, row 0, visual col 0
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        WordDocument doc = new(
        [
            new WordTable([new WordTableRow(
                [new WordTableCell([new WordRun("X")]), new WordTableCell([new WordRun("Y")])])]),
        ]);
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        editor.Find("[aria-label='Merge cell right']").Click();

        WordTableRow row = changed!.Blocks.OfType<WordTable>().Single().Rows[0];
        Assert.Equal(2, row.Cells[0].ColSpan);     // anchor spans both columns
        Assert.Equal(0, row.Cells[1].ColSpan);     // neighbour is now covered
        Assert.Equal("XY", Text(row.Cells[0].Runs)); // content merged into the anchor
    }

    [Fact]
    public void Split_cell_restores_the_covered_column()
    {
        FakeRichTextInterop fake = new() { TableCell = "0,0,0" };
        Services.AddScoped<IRichTextInterop>(_ => fake);

        WordDocument? changed = null;
        WordDocument doc = new(
        [
            new WordTable([new WordTableRow(
                [new WordTableCell([new WordRun("XY")], null, 2), new WordTableCell([], null, 0)])]),
        ]);
        IRenderedComponent<DxWordEditor> editor = RenderComponent<DxWordEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.DocumentChanged, EventCallback.Factory.Create<WordDocument>(this, d => changed = d)));

        editor.Find("[aria-label='Split merged cell']").Click();

        WordTableRow row = changed!.Blocks.OfType<WordTable>().Single().Rows[0];
        Assert.Equal(1, row.Cells[0].ColSpan); // anchor back to a single column
        Assert.Equal(1, row.Cells[1].ColSpan); // covered cell restored
    }
}
