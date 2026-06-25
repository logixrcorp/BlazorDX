using System.Globalization;

namespace BlazorDX.Documents.Formula;

/// <summary>
/// The built-in worksheet functions. Every function is a plain static method
/// dispatched by an upper-cased name through a static switch — no reflection, no
/// delegates table needing trim roots. Each function pulls its arguments through the
/// <see cref="Coercion"/> helpers, so a wrong-typed or error argument propagates as a
/// value rather than throwing.
/// </summary>
/// <remarks>
/// Supported set:
/// <list type="bullet">
/// <item>Math/stats: SUM, AVERAGE, MIN, MAX, COUNT, COUNTA, ABS, ROUND, INT, MOD, SQRT.</item>
/// <item>Logical: IF, AND, OR, NOT.</item>
/// <item>Text: CONCAT, CONCATENATE, LEN, LEFT, RIGHT, MID, UPPER, LOWER, TRIM.</item>
/// </list>
/// An unknown name yields <c>#NAME?</c>; a wrong argument count yields <c>#VALUE!</c>.
/// </remarks>
public static class FunctionLibrary
{
    /// <summary>Dispatches a parsed function call to its implementation.</summary>
    public static CellValue Invoke(Evaluator evaluator, FunctionCallNode call)
    {
        IReadOnlyList<ExpressionNode> args = call.Arguments;
        switch (call.Name)
        {
            // Aggregations consume ranges, so they flatten arguments themselves.
            case "SUM":
                return Sum(evaluator, args);
            case "AVERAGE":
                return Average(evaluator, args);
            case "MIN":
                return MinMax(evaluator, args, isMin: true);
            case "MAX":
                return MinMax(evaluator, args, isMin: false);
            case "COUNT":
                return Count(evaluator, args);
            case "COUNTA":
                return CountA(evaluator, args);

            case "IF":
                return If(evaluator, args);
            case "AND":
                return AndOr(evaluator, args, isAnd: true);
            case "OR":
                return AndOr(evaluator, args, isAnd: false);
            case "NOT":
                return Not(evaluator, args);

            case "ABS":
                return UnaryMath(evaluator, args, Math.Abs);
            case "SQRT":
                return Sqrt(evaluator, args);
            case "INT":
                return UnaryMath(evaluator, args, x => Math.Floor(x));
            case "ROUND":
                return Round(evaluator, args);
            case "MOD":
                return Mod(evaluator, args);

            case "CONCAT":
            case "CONCATENATE":
                return Concat(evaluator, args);
            case "LEN":
                return Len(evaluator, args);
            case "LEFT":
                return LeftRight(evaluator, args, fromLeft: true);
            case "RIGHT":
                return LeftRight(evaluator, args, fromLeft: false);
            case "MID":
                return Mid(evaluator, args);
            case "UPPER":
                return TextTransform(evaluator, args, s => s.ToUpperInvariant());
            case "LOWER":
                return TextTransform(evaluator, args, s => s.ToLowerInvariant());
            case "TRIM":
                return TextTransform(evaluator, args, TrimExcel);

            default:
                return CellValue.Error(FormulaError.Name);
        }
    }

    // ---- Aggregations ------------------------------------------------------

    private static CellValue Sum(Evaluator ev, IReadOnlyList<ExpressionNode> args)
    {
        double total = 0d;
        foreach (ExpressionNode arg in args)
        {
            foreach (CellValue value in ev.EvaluateArgument(arg))
            {
                if (value.IsError)
                {
                    return value;
                }

                // Text and blanks inside an aggregation are skipped, matching Excel.
                if (value.Kind is CellValueKind.Number)
                {
                    total += value.AsRawNumber;
                }
                else if (value.Kind is CellValueKind.Boolean)
                {
                    total += value.AsRawBool ? 1d : 0d;
                }
            }
        }

        return CellValue.Number(total);
    }

    private static CellValue Average(Evaluator ev, IReadOnlyList<ExpressionNode> args)
    {
        double total = 0d;
        int count = 0;
        foreach (ExpressionNode arg in args)
        {
            foreach (CellValue value in ev.EvaluateArgument(arg))
            {
                if (value.IsError)
                {
                    return value;
                }

                if (value.Kind is CellValueKind.Number)
                {
                    total += value.AsRawNumber;
                    count++;
                }
                else if (value.Kind is CellValueKind.Boolean)
                {
                    total += value.AsRawBool ? 1d : 0d;
                    count++;
                }
            }
        }

        if (count == 0)
        {
            return CellValue.Error(FormulaError.Div0);
        }

        return CellValue.Number(total / count);
    }

    private static CellValue MinMax(Evaluator ev, IReadOnlyList<ExpressionNode> args, bool isMin)
    {
        double extreme = isMin ? double.PositiveInfinity : double.NegativeInfinity;
        int count = 0;
        foreach (ExpressionNode arg in args)
        {
            foreach (CellValue value in ev.EvaluateArgument(arg))
            {
                if (value.IsError)
                {
                    return value;
                }

                double? n = value.Kind switch
                {
                    CellValueKind.Number => value.AsRawNumber,
                    CellValueKind.Boolean => value.AsRawBool ? 1d : 0d,
                    _ => null,
                };

                if (n is double num)
                {
                    extreme = isMin ? Math.Min(extreme, num) : Math.Max(extreme, num);
                    count++;
                }
            }
        }

        return count == 0 ? CellValue.Number(0d) : CellValue.Number(extreme);
    }

    private static CellValue Count(Evaluator ev, IReadOnlyList<ExpressionNode> args)
    {
        int count = 0;
        foreach (ExpressionNode arg in args)
        {
            foreach (CellValue value in ev.EvaluateArgument(arg))
            {
                // COUNT counts numbers only (errors do not propagate from COUNT).
                if (value.Kind is CellValueKind.Number)
                {
                    count++;
                }
            }
        }

        return CellValue.Number(count);
    }

    private static CellValue CountA(Evaluator ev, IReadOnlyList<ExpressionNode> args)
    {
        int count = 0;
        foreach (ExpressionNode arg in args)
        {
            foreach (CellValue value in ev.EvaluateArgument(arg))
            {
                // COUNTA counts every non-blank cell, including text and errors.
                if (value.Kind != CellValueKind.Blank)
                {
                    count++;
                }
            }
        }

        return CellValue.Number(count);
    }

    // ---- Logical -----------------------------------------------------------

    private static CellValue If(Evaluator ev, IReadOnlyList<ExpressionNode> args)
    {
        if (args.Count is < 2 or > 3)
        {
            return CellValue.Error(FormulaError.Value);
        }

        CellValue condition = ev.Evaluate(args[0]);
        if (condition.IsError)
        {
            return condition;
        }

        if (!Coercion.TryToBool(condition, out bool truthy, out CellValue error))
        {
            return error;
        }

        if (truthy)
        {
            return ev.Evaluate(args[1]);
        }

        return args.Count == 3 ? ev.Evaluate(args[2]) : CellValue.Bool(false);
    }

    private static CellValue AndOr(Evaluator ev, IReadOnlyList<ExpressionNode> args, bool isAnd)
    {
        if (args.Count == 0)
        {
            return CellValue.Error(FormulaError.Value);
        }

        bool any = false;
        bool acc = isAnd;
        foreach (ExpressionNode arg in args)
        {
            foreach (CellValue value in ev.EvaluateArgument(arg))
            {
                if (value.IsError)
                {
                    return value;
                }

                // Text that is neither a number nor TRUE/FALSE is ignored, as in Excel.
                if (value.Kind == CellValueKind.Text &&
                    !Coercion.TryToBool(value, out _, out _))
                {
                    continue;
                }

                if (!Coercion.TryToBool(value, out bool b, out CellValue err))
                {
                    return err;
                }

                any = true;
                acc = isAnd ? acc && b : acc || b;
            }
        }

        if (!any)
        {
            return CellValue.Error(FormulaError.Value);
        }

        return CellValue.Bool(acc);
    }

    private static CellValue Not(Evaluator ev, IReadOnlyList<ExpressionNode> args)
    {
        if (args.Count != 1)
        {
            return CellValue.Error(FormulaError.Value);
        }

        CellValue value = ev.Evaluate(args[0]);
        if (value.IsError)
        {
            return value;
        }

        if (!Coercion.TryToBool(value, out bool b, out CellValue error))
        {
            return error;
        }

        return CellValue.Bool(!b);
    }

    // ---- Math --------------------------------------------------------------

    private static CellValue UnaryMath(Evaluator ev, IReadOnlyList<ExpressionNode> args, Func<double, double> fn)
    {
        if (args.Count != 1)
        {
            return CellValue.Error(FormulaError.Value);
        }

        if (!ScalarNumber(ev, args[0], out double x, out CellValue error))
        {
            return error;
        }

        return CellValue.Number(fn(x));
    }

    private static CellValue Sqrt(Evaluator ev, IReadOnlyList<ExpressionNode> args)
    {
        if (args.Count != 1)
        {
            return CellValue.Error(FormulaError.Value);
        }

        if (!ScalarNumber(ev, args[0], out double x, out CellValue error))
        {
            return error;
        }

        if (x < 0d)
        {
            return CellValue.Error(FormulaError.Num);
        }

        return CellValue.Number(Math.Sqrt(x));
    }

    private static CellValue Round(Evaluator ev, IReadOnlyList<ExpressionNode> args)
    {
        if (args.Count != 2)
        {
            return CellValue.Error(FormulaError.Value);
        }

        if (!ScalarNumber(ev, args[0], out double number, out CellValue e1))
        {
            return e1;
        }

        if (!ScalarNumber(ev, args[1], out double digitsRaw, out CellValue e2))
        {
            return e2;
        }

        int digits = (int)digitsRaw;
        // Excel ROUND rounds half away from zero and supports negative digits.
        double factor = Math.Pow(10d, digits);
        double scaled = number * factor;
        double rounded = Math.Round(scaled, MidpointRounding.AwayFromZero);
        return CellValue.Number(rounded / factor);
    }

    private static CellValue Mod(Evaluator ev, IReadOnlyList<ExpressionNode> args)
    {
        if (args.Count != 2)
        {
            return CellValue.Error(FormulaError.Value);
        }

        if (!ScalarNumber(ev, args[0], out double number, out CellValue e1))
        {
            return e1;
        }

        if (!ScalarNumber(ev, args[1], out double divisor, out CellValue e2))
        {
            return e2;
        }

        if (divisor == 0d)
        {
            return CellValue.Error(FormulaError.Div0);
        }

        // Excel MOD result takes the sign of the divisor (floored modulo).
        double result = number - (divisor * Math.Floor(number / divisor));
        return CellValue.Number(result);
    }

    // ---- Text --------------------------------------------------------------

    private static CellValue Concat(Evaluator ev, IReadOnlyList<ExpressionNode> args)
    {
        var sb = new System.Text.StringBuilder();
        foreach (ExpressionNode arg in args)
        {
            foreach (CellValue value in ev.EvaluateArgument(arg))
            {
                if (value.IsError)
                {
                    return value;
                }

                if (!Coercion.TryToText(value, out string text, out CellValue error))
                {
                    return error;
                }

                sb.Append(text);
            }
        }

        return CellValue.Text(sb.ToString());
    }

    private static CellValue Len(Evaluator ev, IReadOnlyList<ExpressionNode> args)
    {
        if (args.Count != 1)
        {
            return CellValue.Error(FormulaError.Value);
        }

        if (!ScalarText(ev, args[0], out string text, out CellValue error))
        {
            return error;
        }

        return CellValue.Number(text.Length);
    }

    private static CellValue LeftRight(Evaluator ev, IReadOnlyList<ExpressionNode> args, bool fromLeft)
    {
        if (args.Count is < 1 or > 2)
        {
            return CellValue.Error(FormulaError.Value);
        }

        if (!ScalarText(ev, args[0], out string text, out CellValue te))
        {
            return te;
        }

        int count = 1;
        if (args.Count == 2)
        {
            if (!ScalarNumber(ev, args[1], out double raw, out CellValue ne))
            {
                return ne;
            }

            count = (int)raw;
        }

        if (count < 0)
        {
            return CellValue.Error(FormulaError.Value);
        }

        if (count >= text.Length)
        {
            return CellValue.Text(text);
        }

        string slice = fromLeft ? text[..count] : text[(text.Length - count)..];
        return CellValue.Text(slice);
    }

    private static CellValue Mid(Evaluator ev, IReadOnlyList<ExpressionNode> args)
    {
        if (args.Count != 3)
        {
            return CellValue.Error(FormulaError.Value);
        }

        if (!ScalarText(ev, args[0], out string text, out CellValue te))
        {
            return te;
        }

        if (!ScalarNumber(ev, args[1], out double startRaw, out CellValue se))
        {
            return se;
        }

        if (!ScalarNumber(ev, args[2], out double lenRaw, out CellValue le))
        {
            return le;
        }

        int start = (int)startRaw;
        int length = (int)lenRaw;
        if (start < 1 || length < 0)
        {
            return CellValue.Error(FormulaError.Value);
        }

        int zeroBased = start - 1;
        if (zeroBased >= text.Length)
        {
            return CellValue.Text(string.Empty);
        }

        int available = text.Length - zeroBased;
        int take = Math.Min(length, available);
        return CellValue.Text(text.Substring(zeroBased, take));
    }

    private static CellValue TextTransform(
        Evaluator ev,
        IReadOnlyList<ExpressionNode> args,
        Func<string, string> transform)
    {
        if (args.Count != 1)
        {
            return CellValue.Error(FormulaError.Value);
        }

        if (!ScalarText(ev, args[0], out string text, out CellValue error))
        {
            return error;
        }

        return CellValue.Text(transform(text));
    }

    /// <summary>
    /// Excel TRIM collapses internal whitespace runs to single spaces and strips the
    /// ends, unlike <see cref="string.Trim()"/> which only strips the ends. Every
    /// whitespace character (tabs, newlines, non-breaking spaces, …) is treated as a
    /// separator, matching Excel — not just the ASCII space.
    /// </summary>
    private static string TrimExcel(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        bool inWord = false;
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (inWord)
                {
                    pendingSpace = true;
                }

                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
            inWord = true;
        }

        return sb.ToString();
    }

    // ---- Scalar helpers ----------------------------------------------------

    private static bool ScalarNumber(Evaluator ev, ExpressionNode node, out double value, out CellValue error)
    {
        CellValue cell = ev.Evaluate(node);
        if (cell.IsError)
        {
            value = 0d;
            error = cell;
            return false;
        }

        return Coercion.TryToNumber(cell, out value, out error);
    }

    private static bool ScalarText(Evaluator ev, ExpressionNode node, out string value, out CellValue error)
    {
        CellValue cell = ev.Evaluate(node);
        if (cell.IsError)
        {
            value = string.Empty;
            error = cell;
            return false;
        }

        return Coercion.TryToText(cell, out value, out error);
    }
}
