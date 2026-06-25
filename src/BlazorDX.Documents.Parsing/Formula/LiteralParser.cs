using System.Globalization;

namespace BlazorDX.Documents.Formula;

/// <summary>
/// Interprets a non-formula cell's raw text into a typed <see cref="CellValue"/>:
/// empty text is blank, <c>TRUE</c>/<c>FALSE</c> become booleans, number-looking text
/// becomes a number (invariant culture), and an error sigil such as <c>#DIV/0!</c> is
/// recognised as the corresponding error. Anything else is text.
/// </summary>
public static class LiteralParser
{
    /// <summary>Parses literal cell text into a typed value.</summary>
    public static CellValue Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return CellValue.Blank;
        }

        if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return CellValue.True;
        }

        if (text.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return CellValue.False;
        }

        if (TryParseError(text, out FormulaError error))
        {
            return CellValue.Error(error);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            return CellValue.Number(number);
        }

        return CellValue.Text(text);
    }

    private static bool TryParseError(string text, out FormulaError error)
    {
        switch (text)
        {
            case "#DIV/0!":
                error = FormulaError.Div0;
                return true;
            case "#VALUE!":
                error = FormulaError.Value;
                return true;
            case "#REF!":
                error = FormulaError.Ref;
                return true;
            case "#NAME?":
                error = FormulaError.Name;
                return true;
            case "#N/A":
                error = FormulaError.Na;
                return true;
            case "#CIRC!":
                error = FormulaError.Circular;
                return true;
            case "#NUM!":
                error = FormulaError.Num;
                return true;
            default:
                error = default;
                return false;
        }
    }
}
