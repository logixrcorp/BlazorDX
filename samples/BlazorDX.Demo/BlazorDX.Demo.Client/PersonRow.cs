using BlazorDX.Primitives.Grid;

namespace BlazorDX.Demo.Client;

/// <summary>
/// A demo row type. The <c>[GridRow]</c>/<c>[GridColumn]</c> attributes drive
/// BlazorDX.SourceGen, which emits <c>PersonRowGridAccessor</c> at build time —
/// the grid reads these cells with zero reflection.
/// </summary>
[GridRow]
public sealed class PersonRow
{
    [GridColumn("ID", Order = 0)]
    public int Id { get; set; }

    [GridColumn("Name", Order = 1)]
    public string Name { get; set; } = string.Empty;

    [GridColumn("City", Order = 2)]
    public string City { get; set; } = string.Empty;

    [GridColumn("Score", Order = 3)]
    public double Score { get; set; }

    [GridColumn("Visits", Order = 4)]
    public int Visits { get; set; }
}
