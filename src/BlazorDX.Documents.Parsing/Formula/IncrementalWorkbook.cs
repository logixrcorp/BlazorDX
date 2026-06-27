namespace BlazorDX.Documents.Formula;

/// <summary>
/// A stateful sheet that recalculates <em>incrementally</em>: a single-cell edit re-parses
/// only that cell and re-evaluates only the cells actually affected (the edited cell plus its
/// transitive dependents), in dependency order, instead of re-parsing and re-evaluating the
/// whole sheet. For a localized edit this turns recalc from O(sheet) into O(affected cells).
/// </summary>
/// <remarks>
/// <para>
/// The engine caches each formula's parsed AST and its dependency set, and maintains the
/// reverse graph (which cells reference a given cell) so the dirty set after an edit is a
/// graph walk, not a re-scan. Results match <see cref="WorkbookRecalc"/> cell-for-cell:
/// literals, formulas, range aggregation, and <see cref="FormulaError.Circular"/> for cells
/// in (or fed by) a dependency cycle. Dimensions are fixed at construction; references outside
/// the sheet read as blank.
/// </para>
/// <para>
/// This is the recalc path for an interactive editor. <see cref="WorkbookRecalc"/> remains the
/// one-shot whole-sheet entry point (e.g. on load or import).
/// </para>
/// </remarks>
public sealed class IncrementalWorkbook
{
    private readonly int _rows;
    private readonly int _columns;

    // Per-cell state, indexed by (row * columns + column).
    private readonly bool[] _isFormula;
    private readonly ExpressionNode?[] _ast;
    private readonly CellValue[] _literal;     // value of a non-formula cell
    private readonly CellValue?[] _parseError; // non-null when a formula failed to parse
    private readonly int[][] _deps;            // distinct in-bounds cells each cell references
    private readonly HashSet<int>?[] _dependents; // reverse edges: cells that reference this one

    private readonly ArrayCellGrid _grid;       // current computed values
    private readonly Evaluator _evaluator;

    /// <summary>Builds a workbook from a rectangular grid of cell text and computes every cell.</summary>
    public IncrementalWorkbook(IReadOnlyList<IReadOnlyList<string>> cellText)
    {
        ArgumentNullException.ThrowIfNull(cellText);

        _rows = cellText.Count;
        int columns = 0;
        for (int r = 0; r < _rows; r++)
        {
            int len = cellText[r]?.Count ?? 0;
            if (len > columns)
            {
                columns = len;
            }
        }

        _columns = columns;
        int count = _rows * _columns;

        _isFormula = new bool[count];
        _ast = new ExpressionNode?[count];
        _literal = new CellValue[count];
        _parseError = new CellValue?[count];
        _deps = new int[count][];
        _dependents = new HashSet<int>?[count];
        _grid = new ArrayCellGrid(_rows, _columns);
        _evaluator = new Evaluator(_grid);

        for (int r = 0; r < _rows; r++)
        {
            IReadOnlyList<string>? row = cellText[r];
            for (int c = 0; c < _columns; c++)
            {
                string text = (row is not null && c < row.Count) ? row[c] ?? string.Empty : string.Empty;
                Parse((r * _columns) + c, text);
            }
        }

        // Build the reverse graph, then compute the whole sheet once.
        for (int index = 0; index < count; index++)
        {
            AddReverseEdges(index);
        }

        var all = new HashSet<int>(count);
        for (int index = 0; index < count; index++)
        {
            all.Add(index);
        }

        Evaluate(OrderForEvaluation(all), all);
    }

    /// <summary>The number of rows.</summary>
    public int RowCount => _rows;

    /// <summary>The number of columns.</summary>
    public int ColumnCount => _columns;

    /// <summary>The current computed value at the given coordinates (blank if out of bounds).</summary>
    public CellValue GetValue(int row, int column) => _grid.GetValue(new CellAddress(row, column));

    /// <summary>
    /// Edits one cell's text and re-evaluates only the cells it affects (itself plus its
    /// transitive dependents), in dependency order. Out-of-bounds coordinates are ignored.
    /// </summary>
    public void SetCell(int row, int column, string text)
    {
        if (row < 0 || row >= _rows || column < 0 || column >= _columns)
        {
            return;
        }

        int index = (row * _columns) + column;

        // Re-parse this one cell, swapping its edges in the reverse graph.
        RemoveReverseEdges(index);
        Parse(index, text ?? string.Empty);
        AddReverseEdges(index);

        // Dirty set = the edited cell plus everything that (transitively) reads from it.
        var dirty = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(index);
        while (stack.Count > 0)
        {
            int current = stack.Pop();
            if (!dirty.Add(current))
            {
                continue;
            }

            HashSet<int>? readers = _dependents[current];
            if (readers is null)
            {
                continue;
            }

            foreach (int reader in readers)
            {
                if (!dirty.Contains(reader))
                {
                    stack.Push(reader);
                }
            }
        }

        Evaluate(OrderForEvaluation(dirty), dirty);
    }

    /// <summary>Snapshots every computed value into a jagged grid of the workbook's dimensions.</summary>
    public CellValue[][] ToValues()
    {
        var jagged = new CellValue[_rows][];
        for (int r = 0; r < _rows; r++)
        {
            var row = new CellValue[_columns];
            for (int c = 0; c < _columns; c++)
            {
                row[c] = _grid.GetValue(new CellAddress(r, c));
            }

            jagged[r] = row;
        }

        return jagged;
    }

    // ---- Internals ---------------------------------------------------------------

    private void Parse(int index, string text)
    {
        if (text.Length > 0 && text[0] == '=')
        {
            _isFormula[index] = true;
            try
            {
                ExpressionNode ast = Parser.Parse(text);
                _ast[index] = ast;
                _parseError[index] = null;
                _deps[index] = CollectDependencies(ast);
            }
            catch (FormulaSyntaxException)
            {
                _ast[index] = null;
                _parseError[index] = CellValue.Error(FormulaError.Name);
                _deps[index] = [];
            }
        }
        else
        {
            _isFormula[index] = false;
            _ast[index] = null;
            _parseError[index] = null;
            _literal[index] = LiteralParser.Parse(text);
            _deps[index] = [];
        }
    }

    private void AddReverseEdges(int index)
    {
        foreach (int dep in _deps[index])
        {
            if (dep != index)
            {
                (_dependents[dep] ??= new HashSet<int>()).Add(index);
            }
        }
    }

    private void RemoveReverseEdges(int index)
    {
        foreach (int dep in _deps[index])
        {
            if (dep != index)
            {
                _dependents[dep]?.Remove(index);
            }
        }
    }

    // Topologically orders the dirty subset using only edges among dirty cells (clean
    // dependencies are already up to date). Cells left unordered are in a cycle.
    private List<int> OrderForEvaluation(HashSet<int> dirty)
    {
        var inDegree = new Dictionary<int, int>(dirty.Count);
        foreach (int index in dirty)
        {
            int degree = 0;
            foreach (int dep in _deps[index])
            {
                if (dep != index && dirty.Contains(dep))
                {
                    degree++;
                }
            }

            inDegree[index] = degree;
        }

        var queue = new Queue<int>();
        foreach (int index in dirty)
        {
            if (inDegree[index] == 0 && !IsSelfReferential(index))
            {
                queue.Enqueue(index);
            }
        }

        var order = new List<int>(dirty.Count);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            order.Add(current);

            HashSet<int>? readers = _dependents[current];
            if (readers is null)
            {
                continue;
            }

            foreach (int reader in readers)
            {
                if (dirty.Contains(reader) && --inDegree[reader] == 0 && !IsSelfReferential(reader))
                {
                    queue.Enqueue(reader);
                }
            }
        }

        return order;
    }

    private void Evaluate(List<int> order, HashSet<int> dirty)
    {
        foreach (int index in order)
        {
            WriteValue(index);
        }

        // Anything dirty but not ordered sits in (or feeds) a cycle.
        if (order.Count != dirty.Count)
        {
            var ordered = new HashSet<int>(order);
            foreach (int index in dirty)
            {
                if (!ordered.Contains(index))
                {
                    SetCellValue(index, CellValue.Error(FormulaError.Circular));
                }
            }
        }
    }

    private void WriteValue(int index)
    {
        if (!_isFormula[index])
        {
            SetCellValue(index, _literal[index]);
        }
        else if (_parseError[index] is CellValue parseError)
        {
            SetCellValue(index, parseError);
        }
        else
        {
            SetCellValue(index, _evaluator.Evaluate(_ast[index]!));
        }
    }

    private void SetCellValue(int index, CellValue value) =>
        _grid.SetValue(index / _columns, index % _columns, value);

    private bool IsSelfReferential(int index)
    {
        foreach (int dep in _deps[index])
        {
            if (dep == index)
            {
                return true;
            }
        }

        return false;
    }

    // Distinct in-bounds cell indices an expression references (cells and every cell of a
    // range). Deduplicated so the reverse graph and in-degree counts stay exact.
    private int[] CollectDependencies(ExpressionNode node)
    {
        var deps = new HashSet<int>();
        Walk(node, deps);
        if (deps.Count == 0)
        {
            return [];
        }

        var array = new int[deps.Count];
        deps.CopyTo(array);
        return array;
    }

    private void Walk(ExpressionNode node, HashSet<int> deps)
    {
        switch (node)
        {
            case CellReferenceNode cell:
                AddIfInBounds(cell.Address, deps);
                break;
            case RangeNode range:
                foreach (CellAddress addr in range.Range.Addresses())
                {
                    AddIfInBounds(addr, deps);
                }

                break;
            case UnaryMinusNode unary:
                Walk(unary.Operand, deps);
                break;
            case BinaryNode binary:
                Walk(binary.Left, deps);
                Walk(binary.Right, deps);
                break;
            case FunctionCallNode call:
                foreach (ExpressionNode arg in call.Arguments)
                {
                    Walk(arg, deps);
                }

                break;
            default:
                break;
        }
    }

    private void AddIfInBounds(CellAddress addr, HashSet<int> deps)
    {
        if (addr.Row >= 0 && addr.Row < _rows && addr.Column >= 0 && addr.Column < _columns)
        {
            deps.Add((addr.Row * _columns) + addr.Column);
        }
    }
}
