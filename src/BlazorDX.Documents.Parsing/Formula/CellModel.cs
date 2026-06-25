using System.Globalization;

namespace BlazorDX.Documents.Formula;

/// <summary>
/// The kind of value a cell or formula evaluates to. Errors are first-class
/// values that propagate through expressions, exactly as in a real spreadsheet.
/// </summary>
public enum CellValueKind
{
    /// <summary>An empty cell. Coerces to <c>0</c> in numeric context and <c>""</c> in text context.</summary>
    Blank,

    /// <summary>A floating-point number.</summary>
    Number,

    /// <summary>A text string.</summary>
    Text,

    /// <summary>A boolean (<c>TRUE</c>/<c>FALSE</c>).</summary>
    Boolean,

    /// <summary>An error value such as <c>#DIV/0!</c>.</summary>
    Error,
}

/// <summary>
/// The closed set of spreadsheet error values the engine produces and propagates.
/// </summary>
public enum FormulaError
{
    /// <summary>Division by zero (<c>#DIV/0!</c>).</summary>
    Div0,

    /// <summary>A value of the wrong type was used (<c>#VALUE!</c>).</summary>
    Value,

    /// <summary>An invalid cell reference (<c>#REF!</c>).</summary>
    Ref,

    /// <summary>An unknown function or name (<c>#NAME?</c>).</summary>
    Name,

    /// <summary>A value is not available (<c>#N/A</c>).</summary>
    Na,

    /// <summary>A circular reference was detected (<c>#CIRC!</c>).</summary>
    Circular,

    /// <summary>A number could not be represented (<c>#NUM!</c>).</summary>
    Num,
}

/// <summary>
/// An immutable, typed cell value. This is the single currency of the evaluator:
/// every cell reference resolves to one, every function consumes and produces them,
/// and errors travel as values rather than exceptions so they can propagate.
/// </summary>
public readonly struct CellValue : IEquatable<CellValue>
{
    private readonly double _number;
    private readonly string? _text;
    private readonly bool _boolean;
    private readonly FormulaError _error;

    private CellValue(CellValueKind kind, double number, string? text, bool boolean, FormulaError error)
    {
        Kind = kind;
        _number = number;
        _text = text;
        _boolean = boolean;
        _error = error;
    }

    /// <summary>The shared empty value.</summary>
    public static CellValue Blank { get; } = new(CellValueKind.Blank, 0d, null, false, default);

    /// <summary>The canonical <c>TRUE</c>.</summary>
    public static CellValue True { get; } = new(CellValueKind.Boolean, 0d, null, true, default);

    /// <summary>The canonical <c>FALSE</c>.</summary>
    public static CellValue False { get; } = new(CellValueKind.Boolean, 0d, null, false, default);

    /// <summary>The discriminator for this value.</summary>
    public CellValueKind Kind { get; }

    /// <summary>Creates a number value.</summary>
    public static CellValue Number(double value) =>
        new(CellValueKind.Number, value, null, false, default);

    /// <summary>Creates a text value.</summary>
    public static CellValue Text(string value) =>
        new(CellValueKind.Text, 0d, value ?? string.Empty, false, default);

    /// <summary>Creates a boolean value.</summary>
    public static CellValue Bool(bool value) => value ? True : False;

    /// <summary>Creates an error value.</summary>
    public static CellValue Error(FormulaError error) =>
        new(CellValueKind.Error, 0d, null, false, error);

    /// <summary>Whether this value is an error.</summary>
    public bool IsError => Kind == CellValueKind.Error;

    /// <summary>The underlying error (only meaningful when <see cref="IsError"/>).</summary>
    public FormulaError ErrorValue => _error;

    /// <summary>The raw number payload (only meaningful when <see cref="Kind"/> is Number).</summary>
    public double AsRawNumber => _number;

    /// <summary>The raw boolean payload (only meaningful when <see cref="Kind"/> is Boolean).</summary>
    public bool AsRawBool => _boolean;

    /// <summary>The raw text payload (only meaningful when <see cref="Kind"/> is Text).</summary>
    public string AsRawText => _text ?? string.Empty;

    /// <summary>
    /// Renders the value the way a spreadsheet cell would display it: numbers via
    /// the invariant culture, booleans as <c>TRUE</c>/<c>FALSE</c>, errors by their
    /// sigil (<c>#DIV/0!</c> etc.), and blanks as the empty string.
    /// </summary>
    public string ToDisplayString() => Kind switch
    {
        CellValueKind.Blank => string.Empty,
        CellValueKind.Number => _number.ToString("R", CultureInfo.InvariantCulture),
        CellValueKind.Text => _text ?? string.Empty,
        CellValueKind.Boolean => _boolean ? "TRUE" : "FALSE",
        CellValueKind.Error => ErrorText(_error),
        _ => string.Empty,
    };

    /// <summary>The textual sigil for an error value, e.g. <c>#DIV/0!</c>.</summary>
    public static string ErrorText(FormulaError error) => error switch
    {
        FormulaError.Div0 => "#DIV/0!",
        FormulaError.Value => "#VALUE!",
        FormulaError.Ref => "#REF!",
        FormulaError.Name => "#NAME?",
        FormulaError.Na => "#N/A",
        FormulaError.Circular => "#CIRC!",
        FormulaError.Num => "#NUM!",
        _ => "#ERROR!",
    };

    /// <inheritdoc />
    public bool Equals(CellValue other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            CellValueKind.Number => _number.Equals(other._number),
            CellValueKind.Text => string.Equals(_text, other._text, StringComparison.Ordinal),
            CellValueKind.Boolean => _boolean == other._boolean,
            CellValueKind.Error => _error == other._error,
            _ => true,
        };
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CellValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Kind switch
    {
        CellValueKind.Number => _number.GetHashCode(),
        CellValueKind.Text => StringComparer.Ordinal.GetHashCode(_text ?? string.Empty),
        CellValueKind.Boolean => _boolean.GetHashCode(),
        CellValueKind.Error => _error.GetHashCode(),
        _ => 0,
    };

    /// <inheritdoc />
    public override string ToString() => ToDisplayString();
}
