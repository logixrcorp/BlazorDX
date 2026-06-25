using System.Globalization;

namespace BlazorDX.Documents.Formula;

/// <summary>
/// Type-coercion rules shared by the evaluator and the function library. These mirror
/// the spreadsheet conventions: blanks act as <c>0</c> / <c>""</c>, booleans coerce to
/// <c>1</c>/<c>0</c> numerically, and numeric text coerces to numbers — but arbitrary
/// text in a numeric context yields <c>#VALUE!</c> rather than throwing.
/// </summary>
public static class Coercion
{
    /// <summary>
    /// Coerces a value to a number. On failure returns <c>false</c> and sets
    /// <paramref name="error"/> to the value error to propagate.
    /// </summary>
    public static bool TryToNumber(in CellValue value, out double number, out CellValue error)
    {
        error = CellValue.Blank;
        switch (value.Kind)
        {
            case CellValueKind.Number:
                number = value.AsRawNumber;
                return true;
            case CellValueKind.Blank:
                number = 0d;
                return true;
            case CellValueKind.Boolean:
                number = value.AsRawBool ? 1d : 0d;
                return true;
            case CellValueKind.Text:
                string text = value.AsRawText.Trim();
                if (text.Length > 0 &&
                    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                {
                    number = parsed;
                    return true;
                }

                number = 0d;
                error = CellValue.Error(FormulaError.Value);
                return false;
            case CellValueKind.Error:
                number = 0d;
                error = value;
                return false;
            default:
                number = 0d;
                error = CellValue.Error(FormulaError.Value);
                return false;
        }
    }

    /// <summary>Coerces a value to its text form (blank → <c>""</c>).</summary>
    public static bool TryToText(in CellValue value, out string text, out CellValue error)
    {
        error = CellValue.Blank;
        if (value.IsError)
        {
            text = string.Empty;
            error = value;
            return false;
        }

        text = value.Kind switch
        {
            CellValueKind.Blank => string.Empty,
            CellValueKind.Text => value.AsRawText,
            CellValueKind.Number => value.AsRawNumber.ToString("R", CultureInfo.InvariantCulture),
            CellValueKind.Boolean => value.AsRawBool ? "TRUE" : "FALSE",
            _ => string.Empty,
        };
        return true;
    }

    /// <summary>
    /// Coerces a value to a boolean for logical functions: numbers are truthy when
    /// non-zero, blanks are <c>FALSE</c>, and the literal text <c>TRUE</c>/<c>FALSE</c>
    /// is honoured. Other text is a <c>#VALUE!</c>.
    /// </summary>
    public static bool TryToBool(in CellValue value, out bool result, out CellValue error)
    {
        error = CellValue.Blank;
        switch (value.Kind)
        {
            case CellValueKind.Boolean:
                result = value.AsRawBool;
                return true;
            case CellValueKind.Number:
                result = value.AsRawNumber != 0d;
                return true;
            case CellValueKind.Blank:
                result = false;
                return true;
            case CellValueKind.Text:
                string t = value.AsRawText.Trim();
                if (t.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                {
                    result = true;
                    return true;
                }

                if (t.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                {
                    result = false;
                    return true;
                }

                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
                {
                    result = num != 0d;
                    return true;
                }

                result = false;
                error = CellValue.Error(FormulaError.Value);
                return false;
            case CellValueKind.Error:
                result = false;
                error = value;
                return false;
            default:
                result = false;
                error = CellValue.Error(FormulaError.Value);
                return false;
        }
    }

    /// <summary>
    /// Orders two values for comparison operators. Returns a negative, zero, or
    /// positive number. Numbers (and numeric blanks/booleans) compare numerically;
    /// otherwise both sides compare as text, case-insensitively as Excel does.
    /// </summary>
    public static int Compare(in CellValue left, in CellValue right)
    {
        bool leftNumeric = IsNumericLike(left);
        bool rightNumeric = IsNumericLike(right);

        if (leftNumeric && rightNumeric)
        {
            double l = ToNumberLoose(left);
            double r = ToNumberLoose(right);
            return l.CompareTo(r);
        }

        string ls = ToTextLoose(left);
        string rs = ToTextLoose(right);
        return string.Compare(ls, rs, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumericLike(in CellValue value) =>
        value.Kind is CellValueKind.Number or CellValueKind.Boolean or CellValueKind.Blank;

    private static double ToNumberLoose(in CellValue value) => value.Kind switch
    {
        CellValueKind.Number => value.AsRawNumber,
        CellValueKind.Boolean => value.AsRawBool ? 1d : 0d,
        _ => 0d,
    };

    private static string ToTextLoose(in CellValue value) => value.Kind switch
    {
        CellValueKind.Text => value.AsRawText,
        CellValueKind.Boolean => value.AsRawBool ? "TRUE" : "FALSE",
        CellValueKind.Number => value.AsRawNumber.ToString("R", CultureInfo.InvariantCulture),
        _ => string.Empty,
    };
}
