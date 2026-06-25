namespace BlazorDX.Documents.Formula;

/// <summary>
/// Walks a parsed <see cref="ExpressionNode"/> tree and produces a typed
/// <see cref="CellValue"/>, resolving cell and range references against a supplied
/// <see cref="ICellGrid"/>. Errors are values: any operand error short-circuits and
/// propagates, so a single <c>#DIV/0!</c> deep in an expression surfaces at the top.
/// </summary>
public sealed class Evaluator
{
    private readonly ICellGrid _grid;

    /// <summary>Creates an evaluator bound to a grid of already-computed values.</summary>
    public Evaluator(ICellGrid grid) => _grid = grid;

    /// <summary>Evaluates a node to a scalar cell value.</summary>
    public CellValue Evaluate(ExpressionNode node)
    {
        switch (node)
        {
            case NumberNode n:
                return CellValue.Number(n.Value);
            case TextNode t:
                return CellValue.Text(t.Value);
            case BooleanNode b:
                return CellValue.Bool(b.Value);
            case CellReferenceNode c:
                return _grid.GetValue(c.Address);
            case RangeNode:
                // A bare range used in scalar position is not valid; functions
                // consume ranges via EvaluateRange instead.
                return CellValue.Error(FormulaError.Value);
            case UnaryMinusNode u:
                return EvaluateUnaryMinus(u);
            case BinaryNode bin:
                return EvaluateBinary(bin);
            case FunctionCallNode f:
                return FunctionLibrary.Invoke(this, f);
            default:
                return CellValue.Error(FormulaError.Value);
        }
    }

    /// <summary>
    /// Expands an argument into the flat list of values a function should see: a
    /// range yields every cell, any other expression yields its single scalar value.
    /// </summary>
    public IReadOnlyList<CellValue> EvaluateArgument(ExpressionNode node)
    {
        if (node is RangeNode range)
        {
            var values = new List<CellValue>(range.Range.RowCount * range.Range.ColumnCount);
            foreach (CellAddress addr in range.Range.Addresses())
            {
                values.Add(_grid.GetValue(addr));
            }

            return values;
        }

        return new[] { Evaluate(node) };
    }

    private CellValue EvaluateUnaryMinus(UnaryMinusNode node)
    {
        CellValue operand = Evaluate(node.Operand);
        if (operand.IsError)
        {
            return operand;
        }

        if (!Coercion.TryToNumber(operand, out double value, out CellValue error))
        {
            return error;
        }

        return CellValue.Number(-value);
    }

    private CellValue EvaluateBinary(BinaryNode node)
    {
        CellValue left = Evaluate(node.Left);
        if (left.IsError)
        {
            return left;
        }

        CellValue right = Evaluate(node.Right);
        if (right.IsError)
        {
            return right;
        }

        return node.Operator switch
        {
            BinaryOperator.Concat => EvaluateConcat(left, right),
            BinaryOperator.Equal => CellValue.Bool(Coercion.Compare(left, right) == 0),
            BinaryOperator.NotEqual => CellValue.Bool(Coercion.Compare(left, right) != 0),
            BinaryOperator.LessThan => CellValue.Bool(Coercion.Compare(left, right) < 0),
            BinaryOperator.GreaterThan => CellValue.Bool(Coercion.Compare(left, right) > 0),
            BinaryOperator.LessThanOrEqual => CellValue.Bool(Coercion.Compare(left, right) <= 0),
            BinaryOperator.GreaterThanOrEqual => CellValue.Bool(Coercion.Compare(left, right) >= 0),
            _ => EvaluateArithmetic(node.Operator, left, right),
        };
    }

    private static CellValue EvaluateConcat(in CellValue left, in CellValue right)
    {
        if (!Coercion.TryToText(left, out string l, out CellValue le))
        {
            return le;
        }

        if (!Coercion.TryToText(right, out string r, out CellValue re))
        {
            return re;
        }

        return CellValue.Text(l + r);
    }

    private static CellValue EvaluateArithmetic(BinaryOperator op, in CellValue left, in CellValue right)
    {
        if (!Coercion.TryToNumber(left, out double l, out CellValue le))
        {
            return le;
        }

        if (!Coercion.TryToNumber(right, out double r, out CellValue re))
        {
            return re;
        }

        switch (op)
        {
            case BinaryOperator.Add:
                return CellValue.Number(l + r);
            case BinaryOperator.Subtract:
                return CellValue.Number(l - r);
            case BinaryOperator.Multiply:
                return CellValue.Number(l * r);
            case BinaryOperator.Divide:
                if (r == 0d)
                {
                    return CellValue.Error(FormulaError.Div0);
                }

                return CellValue.Number(l / r);
            case BinaryOperator.Power:
                double result = Math.Pow(l, r);
                if (double.IsNaN(result) || double.IsInfinity(result))
                {
                    return CellValue.Error(FormulaError.Num);
                }

                return CellValue.Number(result);
            default:
                return CellValue.Error(FormulaError.Value);
        }
    }
}
