namespace BlazorDX.Documents.Formula;

/// <summary>
/// The public entry point for the formula engine: evaluate a single expression
/// against a grid of values, or recalculate a whole sheet of literals and formulas
/// into a grid of computed values.
/// </summary>
/// <remarks>
/// <para>
/// Two usage modes:
/// </para>
/// <list type="bullet">
/// <item>
/// <see cref="Evaluate(string, ICellGrid)"/> parses and evaluates one formula
/// (with or without a leading <c>=</c>) against an existing, already-computed grid —
/// useful for a live formula bar.
/// </item>
/// <item>
/// <see cref="Recalculate(IReadOnlyList{IReadOnlyList{string}})"/> takes the raw cell
/// text of an entire sheet (formulas as <c>=…</c> strings, everything else as
/// literals) and returns the fully computed value grid, resolving dependencies in
/// topological order and flagging circular references.
/// </item>
/// </list>
/// <para>
/// The engine is pure C# with no reflection and no external dependencies. Deferred:
/// cross-sheet references, named ranges, array formulas, and the date/lookup/financial
/// function families; the Rust dependency-graph recalc is a later performance pass.
/// </para>
/// </remarks>
public static class FormulaEngine
{
    /// <summary>
    /// Parses and evaluates a single formula against <paramref name="grid"/>. A
    /// leading <c>=</c> is optional. A syntax error resolves to <c>#NAME?</c> rather
    /// than throwing, so callers always receive a value.
    /// </summary>
    public static CellValue Evaluate(string formula, ICellGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        ExpressionNode ast;
        try
        {
            ast = Parser.Parse(formula);
        }
        catch (FormulaSyntaxException)
        {
            return CellValue.Error(FormulaError.Name);
        }

        return new Evaluator(grid).Evaluate(ast);
    }

    /// <summary>
    /// Convenience overload that evaluates a single formula against an empty grid of
    /// the given size (every referenced cell reads as blank). Handy for formulas with
    /// no cell references, e.g. <c>=SUM(1,2,3)</c>.
    /// </summary>
    public static CellValue Evaluate(string formula) =>
        Evaluate(formula, new ArrayCellGrid(0, 0));

    /// <summary>
    /// Recalculates a sheet given as rows of raw cell text into a grid of typed values
    /// of the same dimensions. See <see cref="WorkbookRecalc"/> for the rules.
    /// </summary>
    public static CellValue[][] Recalculate(IReadOnlyList<IReadOnlyList<string>> cellText)
    {
        ArgumentNullException.ThrowIfNull(cellText);
        return WorkbookRecalc.Recalculate(cellText);
    }

    /// <summary>
    /// Recalculates a sheet given as a simple jagged string array (convenient for
    /// callers that hold a <c>string[][]</c> grid).
    /// </summary>
    public static CellValue[][] Recalculate(string[][] cellText)
    {
        ArgumentNullException.ThrowIfNull(cellText);
        return WorkbookRecalc.Recalculate(cellText);
    }

    /// <summary>
    /// Recalculates a parsed <see cref="Worksheet"/> (the reader's output), treating
    /// each cell's text as a literal or formula, and returns the computed value grid.
    /// This bridges the engine to the existing document model without coupling the two.
    /// </summary>
    public static CellValue[][] Recalculate(Worksheet worksheet)
    {
        ArgumentNullException.ThrowIfNull(worksheet);
        return WorkbookRecalc.Recalculate(worksheet.Rows);
    }
}
