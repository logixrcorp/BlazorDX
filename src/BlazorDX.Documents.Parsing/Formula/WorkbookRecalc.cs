namespace BlazorDX.Documents.Formula;

/// <summary>
/// Recalculates a sheet of mixed literals and formulas into a grid of typed values.
/// </summary>
/// <remarks>
/// <para>
/// A cell whose text begins with <c>=</c> is a formula; any other text is a literal
/// (parsed as a number when it looks like one, as <c>TRUE</c>/<c>FALSE</c> when it is
/// a boolean keyword, otherwise as text; empty cells are blank).
/// </para>
/// <para>
/// Formulas are parsed once, their cell/range dependencies extracted, and the cells
/// evaluated in dependency (topological) order so a formula always sees up-to-date
/// inputs. Any cell that participates in a dependency cycle is assigned
/// <see cref="FormulaError.Circular"/> (<c>#CIRC!</c>) without throwing or recursing
/// without bound. A reference to a cell outside the sheet bounds reads as blank.
/// </para>
/// </remarks>
public static class WorkbookRecalc
{
    private sealed class CellPlan
    {
        public bool IsFormula;
        public ExpressionNode? Ast;
        public CellValue Literal;
        public List<int>? Dependencies;
        public CellValue? ParseError;
    }

    /// <summary>
    /// Recalculates a rectangular grid of cell text into a grid of computed values.
    /// The result has the same dimensions as the input.
    /// </summary>
    public static CellValue[][] Recalculate(IReadOnlyList<IReadOnlyList<string>> cellText)
    {
        int rowCount = cellText.Count;
        int columnCount = 0;
        for (int r = 0; r < rowCount; r++)
        {
            int len = cellText[r]?.Count ?? 0;
            if (len > columnCount)
            {
                columnCount = len;
            }
        }

        var result = new ArrayCellGrid(rowCount, columnCount);
        if (rowCount == 0 || columnCount == 0)
        {
            return ToJagged(result, rowCount, columnCount);
        }

        int cellCount = rowCount * columnCount;
        var plans = new CellPlan[cellCount];

        // Pass 1: classify every cell as literal or formula, parse formulas, and
        // collect each formula's in-bounds dependencies as flat cell indices.
        for (int r = 0; r < rowCount; r++)
        {
            IReadOnlyList<string>? row = cellText[r];
            for (int c = 0; c < columnCount; c++)
            {
                int index = (r * columnCount) + c;
                string text = (row is not null && c < row.Count) ? row[c] ?? string.Empty : string.Empty;
                plans[index] = BuildPlan(text, rowCount, columnCount);
            }
        }

        // Pass 2: topologically order the formula cells; cells in a cycle are flagged.
        int[] order = TopologicalOrder(plans, cellCount, out bool[] inCycle);

        // Pass 3: evaluate in order, writing each result back so later cells read it.
        var evaluator = new Evaluator(result);
        for (int k = 0; k < order.Length; k++)
        {
            int index = order[k];
            int r = index / columnCount;
            int c = index % columnCount;
            CellPlan plan = plans[index];

            if (!plan.IsFormula)
            {
                result.SetValue(r, c, plan.Literal);
                continue;
            }

            if (inCycle[index])
            {
                result.SetValue(r, c, CellValue.Error(FormulaError.Circular));
                continue;
            }

            if (plan.ParseError is CellValue parseError)
            {
                result.SetValue(r, c, parseError);
                continue;
            }

            CellValue value = evaluator.Evaluate(plan.Ast!);
            result.SetValue(r, c, value);
        }

        // Any formula cell not visited (because it sat in a cycle excluded from the
        // order) still needs its circular marker.
        for (int index = 0; index < cellCount; index++)
        {
            if (inCycle[index])
            {
                int r = index / columnCount;
                int c = index % columnCount;
                result.SetValue(r, c, CellValue.Error(FormulaError.Circular));
            }
        }

        return ToJagged(result, rowCount, columnCount);
    }

    private static CellPlan BuildPlan(string text, int rowCount, int columnCount)
    {
        if (text.Length > 0 && text[0] == '=')
        {
            var plan = new CellPlan { IsFormula = true };
            try
            {
                plan.Ast = Parser.Parse(text);
                plan.Dependencies = CollectDependencies(plan.Ast, rowCount, columnCount);
            }
            catch (FormulaSyntaxException)
            {
                // A malformed formula resolves to a name error, like Excel's #NAME?.
                plan.ParseError = CellValue.Error(FormulaError.Name);
                plan.Dependencies = new List<int>();
            }

            return plan;
        }

        return new CellPlan { IsFormula = false, Literal = LiteralParser.Parse(text) };
    }

    private static List<int> CollectDependencies(ExpressionNode node, int rowCount, int columnCount)
    {
        var deps = new List<int>();
        Walk(node, deps, rowCount, columnCount);
        return deps;
    }

    private static void Walk(ExpressionNode node, List<int> deps, int rowCount, int columnCount)
    {
        switch (node)
        {
            case CellReferenceNode c:
                AddIfInBounds(c.Address, deps, rowCount, columnCount);
                break;
            case RangeNode range:
                foreach (CellAddress addr in range.Range.Addresses())
                {
                    AddIfInBounds(addr, deps, rowCount, columnCount);
                }

                break;
            case UnaryMinusNode u:
                Walk(u.Operand, deps, rowCount, columnCount);
                break;
            case BinaryNode b:
                Walk(b.Left, deps, rowCount, columnCount);
                Walk(b.Right, deps, rowCount, columnCount);
                break;
            case FunctionCallNode f:
                foreach (ExpressionNode arg in f.Arguments)
                {
                    Walk(arg, deps, rowCount, columnCount);
                }

                break;
            default:
                break;
        }
    }

    private static void AddIfInBounds(CellAddress addr, List<int> deps, int rowCount, int columnCount)
    {
        if (addr.Row >= 0 && addr.Row < rowCount && addr.Column >= 0 && addr.Column < columnCount)
        {
            deps.Add((addr.Row * columnCount) + addr.Column);
        }
    }

    // Kahn-style topological sort over the formula dependency graph. Edges run from a
    // dependency to its dependent. Any node never reaching in-degree zero is part of a
    // cycle (or depends on one) and is reported via <paramref name="inCycle"/>.
    private static int[] TopologicalOrder(CellPlan[] plans, int cellCount, out bool[] inCycle)
    {
        var dependents = new List<int>[cellCount];
        var inDegree = new int[cellCount];

        for (int index = 0; index < cellCount; index++)
        {
            List<int>? deps = plans[index].Dependencies;
            if (deps is null)
            {
                continue;
            }

            foreach (int dep in deps)
            {
                if (dep == index)
                {
                    // A self-reference (=A1 in A1) is its own cycle.
                    continue;
                }

                (dependents[dep] ??= new List<int>()).Add(index);
                inDegree[index]++;
            }
        }

        var order = new List<int>(cellCount);
        var queue = new Queue<int>();
        for (int index = 0; index < cellCount; index++)
        {
            if (inDegree[index] == 0 && !IsSelfReferential(plans[index], index))
            {
                queue.Enqueue(index);
            }
        }

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            order.Add(current);
            List<int>? outEdges = dependents[current];
            if (outEdges is null)
            {
                continue;
            }

            foreach (int next in outEdges)
            {
                if (--inDegree[next] == 0 && !IsSelfReferential(plans[next], next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        // Whatever did not make it into the order is caught in (or fed by) a cycle.
        inCycle = new bool[cellCount];
        if (order.Count != cellCount)
        {
            var visited = new bool[cellCount];
            foreach (int idx in order)
            {
                visited[idx] = true;
            }

            for (int index = 0; index < cellCount; index++)
            {
                if (!visited[index])
                {
                    inCycle[index] = true;
                }
            }
        }

        return order.ToArray();
    }

    private static bool IsSelfReferential(CellPlan plan, int index)
    {
        List<int>? deps = plan.Dependencies;
        if (deps is null)
        {
            return false;
        }

        foreach (int dep in deps)
        {
            if (dep == index)
            {
                return true;
            }
        }

        return false;
    }

    private static CellValue[][] ToJagged(ArrayCellGrid grid, int rowCount, int columnCount)
    {
        var jagged = new CellValue[rowCount][];
        for (int r = 0; r < rowCount; r++)
        {
            var row = new CellValue[columnCount];
            for (int c = 0; c < columnCount; c++)
            {
                row[c] = grid.GetValue(new CellAddress(r, c));
            }

            jagged[r] = row;
        }

        return jagged;
    }
}
