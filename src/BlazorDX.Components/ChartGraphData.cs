namespace BlazorDX.Components;

/// <summary>One node of a <see cref="DxNetworkGraph"/>, referenced by <see cref="GraphEdge.Source"/>/<see cref="GraphEdge.Target"/>.</summary>
/// <param name="Id">A stable key linking it to <see cref="GraphEdge"/> entries.</param>
/// <param name="Label">The node's display name.</param>
/// <param name="Color">Optional CSS color override; otherwise a palette color (by first-appearance order) is used.</param>
public readonly record struct GraphNode(string Id, string Label, string? Color = null);

/// <summary>One connection between two <see cref="GraphNode"/>s, by id.</summary>
public readonly record struct GraphEdge(string Source, string Target, string? Color = null);

/// <summary>One row for <see cref="DxParallelCoordinates"/>: one value per axis, in the same order as <c>Axes</c>.</summary>
/// <param name="Label">The row's name (shown in its tooltip).</param>
/// <param name="Values">One value per axis — must be the same length as <c>Axes</c>.</param>
/// <param name="Color">Optional CSS color override; otherwise a palette color (by row index) is used.</param>
public readonly record struct ParallelCoordinateRow(string Label, IReadOnlyList<double> Values, string? Color = null);

/// <summary>One word for <see cref="DxWordCloud"/>; <see cref="Weight"/> drives font size.</summary>
public readonly record struct WordCloudEntry(string Text, double Weight, string? Color = null);

/// <summary>One node of a <see cref="DxChordDiagram"/>, referenced by <see cref="ChordLink"/>'s index into <c>Nodes</c>.</summary>
public readonly record struct ChordNode(string Label, string? Color = null);

/// <summary>One flow between two <see cref="ChordNode"/>s, by index into the chart's <c>Nodes</c> list.</summary>
public readonly record struct ChordLink(int From, int To, double Value);
