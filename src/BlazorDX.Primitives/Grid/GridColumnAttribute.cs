namespace BlazorDX.Primitives.Grid;

/// <summary>
/// Marks a row type as bindable by BlazorDX's grid. The
/// <c>BlazorDX.SourceGen</c> generator emits a strongly-typed accessor for each
/// type carrying this attribute, so the grid never reflects over the type at runtime.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class GridRowAttribute : Attribute
{
}

/// <summary>
/// Declares a property as a grid column. The generator reads the header and order
/// and infers whether the column is numeric (and therefore sortable by the Rust
/// kernels) from the property's type.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GridColumnAttribute : Attribute
{
    public GridColumnAttribute(string header)
    {
        Header = header;
    }

    /// <summary>Column header text.</summary>
    public string Header { get; }

    /// <summary>Left-to-right column order; lower comes first.</summary>
    public int Order { get; set; }
}
