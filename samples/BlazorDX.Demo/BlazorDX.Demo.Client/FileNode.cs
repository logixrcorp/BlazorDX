using BlazorDX.Primitives.Grid;

namespace BlazorDX.Demo.Client;

/// <summary>
/// A demo hierarchical node for <c>DxTreeGrid</c>. The <c>[GridColumn]</c>
/// properties become tree-grid columns; <see cref="Children"/> is the hierarchy
/// the tree walks (it is not a column).
/// </summary>
[GridRow]
public sealed class FileNode
{
    [GridColumn("Name", Order = 0)]
    public string Name { get; set; } = string.Empty;

    [GridColumn("Kind", Order = 1)]
    public string Kind { get; set; } = string.Empty;

    [GridColumn("Size", Order = 2)]
    public string Size { get; set; } = string.Empty;

    public IReadOnlyList<FileNode> Children { get; init; } = [];

    public static IReadOnlyList<FileNode> SampleTree() =>
    [
        new FileNode
        {
            Name = "src", Kind = "folder", Size = "—",
            Children =
            [
                new FileNode
                {
                    Name = "BlazorDX.Primitives", Kind = "folder", Size = "—",
                    Children =
                    [
                        new() { Name = "DataGridPrimitive.cs", Kind = "C#", Size = "28 KB" },
                        new() { Name = "TreeGridPrimitive.cs", Kind = "C#", Size = "6 KB" },
                    ],
                },
                new FileNode
                {
                    Name = "BlazorDX.Compute.Rust", Kind = "folder", Size = "—",
                    Children =
                    [
                        new() { Name = "lib.rs", Kind = "Rust", Size = "4 KB" },
                        new() { Name = "chart.rs", Kind = "Rust", Size = "3 KB" },
                    ],
                },
            ],
        },
        new FileNode
        {
            Name = "docs", Kind = "folder", Size = "—",
            Children =
            [
                new() { Name = "ARCHITECTURE.md", Kind = "Markdown", Size = "9 KB" },
                new() { Name = "ROADMAP.md", Kind = "Markdown", Size = "5 KB" },
            ],
        },
        new() { Name = "README.md", Kind = "Markdown", Size = "7 KB" },
    ];
}
