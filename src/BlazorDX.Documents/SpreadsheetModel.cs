namespace BlazorDX.Documents;

/// <summary>
/// A parsed workbook: the ordered sheets read from an <c>.xlsx</c> file. This is the
/// reader's output and the spreadsheet viewer's input — a plain, immutable data model
/// with no reflection and no live file handle.
/// </summary>
/// <param name="Sheets">The worksheets, in workbook order.</param>
public sealed record Workbook(IReadOnlyList<Worksheet> Sheets);

/// <summary>
/// One worksheet: a name plus a dense grid of string cell values. The first row is
/// treated as the header by the viewer. Ragged rows are padded to <see cref="ColumnCount"/>.
/// </summary>
/// <param name="Name">The sheet's display name (the tab label).</param>
/// <param name="Rows">Rows of cell text, top to bottom; each row has <see cref="ColumnCount"/> cells.</param>
/// <param name="ColumnCount">The widest row's cell count (columns are uniform across the sheet).</param>
public sealed record Worksheet(string Name, IReadOnlyList<IReadOnlyList<string>> Rows, int ColumnCount);
