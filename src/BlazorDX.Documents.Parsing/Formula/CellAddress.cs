using System.Globalization;

namespace BlazorDX.Documents.Formula;

/// <summary>
/// A zero-based cell coordinate plus its A1-notation absolute flags. Column 0 is
/// <c>A</c>, row 0 is the first row (displayed as <c>1</c>). The <c>$</c> markers in
/// <c>$A$1</c> are tracked but do not change where a reference points in a single
/// recalc — they exist so addresses round-trip through <see cref="ToA1"/> faithfully.
/// </summary>
public readonly struct CellAddress : IEquatable<CellAddress>
{
    /// <summary>Creates an address from zero-based row/column indices.</summary>
    public CellAddress(int row, int column, bool columnAbsolute = false, bool rowAbsolute = false)
    {
        Row = row;
        Column = column;
        ColumnAbsolute = columnAbsolute;
        RowAbsolute = rowAbsolute;
    }

    /// <summary>Zero-based row index (row <c>1</c> is <c>0</c>).</summary>
    public int Row { get; }

    /// <summary>Zero-based column index (<c>A</c> is <c>0</c>).</summary>
    public int Column { get; }

    /// <summary>Whether the column was written with a leading <c>$</c>.</summary>
    public bool ColumnAbsolute { get; }

    /// <summary>Whether the row was written with a leading <c>$</c>.</summary>
    public bool RowAbsolute { get; }

    /// <summary>
    /// Converts a zero-based column index to its letter label: 0→A, 25→Z, 26→AA,
    /// 27→AB, 701→ZZ, 702→AAA. This is bijective base-26 (no zero digit).
    /// </summary>
    public static string ColumnToLetters(int column)
    {
        if (column < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        // Bijective base-26: there is no "0" digit, so each step subtracts one
        // before taking the remainder. Build right-to-left then reverse.
        Span<char> buffer = stackalloc char[8];
        int index = buffer.Length;
        int value = column;
        do
        {
            int remainder = value % 26;
            buffer[--index] = (char)('A' + remainder);
            value = (value / 26) - 1;
        }
        while (value >= 0);

        return new string(buffer[index..]);
    }

    /// <summary>
    /// Converts a column letter label back to a zero-based index. Accepts upper- or
    /// lower-case. Returns <c>-1</c> for an empty or non-alphabetic label.
    /// </summary>
    public static int LettersToColumn(string letters)
    {
        if (string.IsNullOrEmpty(letters))
        {
            return -1;
        }

        int value = 0;
        foreach (char raw in letters)
        {
            char c = char.ToUpperInvariant(raw);
            if (c < 'A' || c > 'Z')
            {
                return -1;
            }

            value = (value * 26) + (c - 'A' + 1);
        }

        return value - 1;
    }

    /// <summary>Renders this address in A1 notation, preserving any <c>$</c> markers.</summary>
    public string ToA1()
    {
        string col = ColumnToLetters(Column);
        string row = (Row + 1).ToString(CultureInfo.InvariantCulture);
        return string.Concat(
            ColumnAbsolute ? "$" : string.Empty,
            col,
            RowAbsolute ? "$" : string.Empty,
            row);
    }

    /// <summary>
    /// Parses an A1-notation reference (e.g. <c>A1</c>, <c>$B$2</c>, <c>c10</c>) into
    /// an address. Returns <c>false</c> for malformed input rather than throwing.
    /// </summary>
    public static bool TryParse(string text, out CellAddress address)
    {
        address = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        int i = 0;
        bool colAbs = false;
        if (text[i] == '$')
        {
            colAbs = true;
            i++;
        }

        int letterStart = i;
        while (i < text.Length && IsLetter(text[i]))
        {
            i++;
        }

        if (i == letterStart)
        {
            return false;
        }

        string letters = text[letterStart..i];

        bool rowAbs = false;
        if (i < text.Length && text[i] == '$')
        {
            rowAbs = true;
            i++;
        }

        int digitStart = i;
        while (i < text.Length && text[i] >= '0' && text[i] <= '9')
        {
            i++;
        }

        if (i == digitStart || i != text.Length)
        {
            return false;
        }

        int column = LettersToColumn(letters);
        if (column < 0)
        {
            return false;
        }

        if (!int.TryParse(text[digitStart..i], NumberStyles.None, CultureInfo.InvariantCulture, out int row1)
            || row1 < 1)
        {
            return false;
        }

        address = new CellAddress(row1 - 1, column, colAbs, rowAbs);
        return true;
    }

    private static bool IsLetter(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

    /// <inheritdoc />
    public bool Equals(CellAddress other) => Row == other.Row && Column == other.Column;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CellAddress other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (Row * 397) ^ Column;

    /// <inheritdoc />
    public override string ToString() => ToA1();
}

/// <summary>
/// A rectangular block of cells, e.g. <c>A1:B3</c>. Stored as an inclusive
/// top-left / bottom-right pair, normalized so <see cref="Start"/> is always the
/// upper-left corner regardless of how the range was written.
/// </summary>
public readonly struct CellRange : IEquatable<CellRange>
{
    /// <summary>Creates a range from two corners, normalizing their order.</summary>
    public CellRange(CellAddress a, CellAddress b)
    {
        int top = Math.Min(a.Row, b.Row);
        int bottom = Math.Max(a.Row, b.Row);
        int left = Math.Min(a.Column, b.Column);
        int right = Math.Max(a.Column, b.Column);
        Start = new CellAddress(top, left, a.ColumnAbsolute, a.RowAbsolute);
        End = new CellAddress(bottom, right, b.ColumnAbsolute, b.RowAbsolute);
    }

    /// <summary>The upper-left corner (inclusive).</summary>
    public CellAddress Start { get; }

    /// <summary>The lower-right corner (inclusive).</summary>
    public CellAddress End { get; }

    /// <summary>Number of rows the range spans.</summary>
    public int RowCount => End.Row - Start.Row + 1;

    /// <summary>Number of columns the range spans.</summary>
    public int ColumnCount => End.Column - Start.Column + 1;

    /// <summary>
    /// Enumerates every address in the range, row-major (left-to-right, top-to-bottom).
    /// </summary>
    public IEnumerable<CellAddress> Addresses()
    {
        for (int r = Start.Row; r <= End.Row; r++)
        {
            for (int c = Start.Column; c <= End.Column; c++)
            {
                yield return new CellAddress(r, c);
            }
        }
    }

    /// <summary>Renders the range in A1 notation, e.g. <c>A1:B3</c>.</summary>
    public string ToA1() => string.Concat(Start.ToA1(), ":", End.ToA1());

    /// <inheritdoc />
    public bool Equals(CellRange other) => Start.Equals(other.Start) && End.Equals(other.End);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CellRange other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (Start.GetHashCode() * 397) ^ End.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => ToA1();
}
