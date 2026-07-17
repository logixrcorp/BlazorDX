namespace BlazorDX.Components;

/// <summary>
/// A hierarchical data node for <see cref="DxTreemap"/> and <see cref="DxSunburst"/>. A leaf (no
/// <see cref="Children"/>) carries its own <see cref="Value"/>; a parent's effective value is
/// always the sum of its children's, computed by the chart — <see cref="Value"/> on a node that
/// has children is ignored. Doesn't fit the flat <see cref="ChartPoint"/> shape (it's a tree, not
/// a series). Plain record, no reflection.
/// </summary>
/// <param name="Label">The node's name.</param>
/// <param name="Value">The leaf's magnitude; ignored on a node with children.</param>
/// <param name="Color">Optional CSS color override; otherwise a palette color (by top-level sibling index) is used.</param>
/// <param name="Children">Child nodes, or null/empty for a leaf.</param>
public sealed record ChartTreeNode(string Label, double Value = 0, string? Color = null, IReadOnlyList<ChartTreeNode>? Children = null);

/// <summary>One node of a <see cref="DxSankeyChart"/>, referenced by <see cref="SankeyLink.Source"/>/<see cref="SankeyLink.Target"/>.</summary>
/// <param name="Id">A stable key linking it to <see cref="SankeyLink"/> entries.</param>
/// <param name="Label">The node's display name.</param>
/// <param name="Color">Optional CSS color override; otherwise a palette color (by first-appearance order) is used.</param>
public readonly record struct SankeyNode(string Id, string Label, string? Color = null);

/// <summary>One flow between two <see cref="SankeyNode"/>s, by id.</summary>
/// <param name="Source">The originating node's <see cref="SankeyNode.Id"/>.</param>
/// <param name="Target">The receiving node's <see cref="SankeyNode.Id"/>.</param>
/// <param name="Value">The flow's magnitude — sets the ribbon's thickness.</param>
/// <param name="Color">Optional CSS color override; otherwise inherits the source node's color.</param>
public readonly record struct SankeyLink(string Source, string Target, double Value, string? Color = null);
