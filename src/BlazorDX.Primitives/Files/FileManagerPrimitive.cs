using Microsoft.AspNetCore.Components;

namespace BlazorDX.Primitives.Files;

/// <summary>
/// A node in a file tree. Reference identity matters (expand/select track the
/// instance), so this is a class, not a record.
/// </summary>
public sealed class FileSystemEntry
{
    public required string Name { get; init; }

    public bool IsDirectory { get; init; }

    /// <summary>Size in bytes (files only).</summary>
    public long Size { get; init; }

    public DateOnly Modified { get; init; }

    public IReadOnlyList<FileSystemEntry> Children { get; init; } = [];
}

/// <summary>One visible folder row in the navigation tree.</summary>
public readonly record struct FolderRow(FileSystemEntry Entry, int Depth, bool HasSubfolders, bool Expanded, bool Selected);

/// <summary>
/// Tier 1 headless file manager. Maintains the expanded/selected folder state,
/// flattens the folder hierarchy for the navigation pane, exposes the selected
/// folder's contents and its breadcrumb path, and routes "open" actions (drill
/// into a folder, or raise a file-open event). Renders nothing itself.
/// </summary>
public class FileManagerPrimitive : ComponentBase
{
    private readonly HashSet<FileSystemEntry> expanded = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FileSystemEntry, FileSystemEntry?> parents =
        new(ReferenceEqualityComparer.Instance);
    private FileSystemEntry? selected;
    private object? lastRoots;

    [Parameter, EditorRequired] public IReadOnlyList<FileSystemEntry> Roots { get; set; } = [];

    /// <summary>Raised when a file (not a folder) is opened.</summary>
    [Parameter] public EventCallback<FileSystemEntry> OnFileOpen { get; set; }

    /// <summary>Raised when the selected folder changes.</summary>
    [Parameter] public EventCallback<FileSystemEntry?> SelectedFolderChanged { get; set; }

    protected FileSystemEntry? SelectedFolder => selected;

    protected override void OnParametersSet()
    {
        if (!ReferenceEqualityComparer.Instance.Equals(lastRoots, Roots))
        {
            lastRoots = Roots;
            parents.Clear();
            IndexParents(Roots, null);
        }
    }

    private void IndexParents(IReadOnlyList<FileSystemEntry> nodes, FileSystemEntry? parent)
    {
        foreach (FileSystemEntry node in nodes)
        {
            parents[node] = parent;
            if (node.Children.Count > 0)
            {
                IndexParents(node.Children, node);
            }
        }
    }

    protected bool IsExpanded(FileSystemEntry folder) => expanded.Contains(folder);

    protected bool IsSelected(FileSystemEntry folder) => ReferenceEquals(selected, folder);

    protected static bool HasSubfolders(FileSystemEntry folder)
    {
        foreach (FileSystemEntry child in folder.Children)
        {
            if (child.IsDirectory)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The folder hierarchy flattened to the currently-visible rows.</summary>
    protected IReadOnlyList<FolderRow> FolderRows()
    {
        List<FolderRow> rows = new();
        AppendFolders(Roots, 0, rows);
        return rows;
    }

    private void AppendFolders(IReadOnlyList<FileSystemEntry> nodes, int depth, List<FolderRow> rows)
    {
        foreach (FileSystemEntry node in nodes)
        {
            if (!node.IsDirectory)
            {
                continue;
            }

            bool hasSub = HasSubfolders(node);
            bool open = hasSub && expanded.Contains(node);
            rows.Add(new FolderRow(node, depth, hasSub, open, ReferenceEquals(selected, node)));
            if (open)
            {
                AppendFolders(node.Children, depth + 1, rows);
            }
        }
    }

    /// <summary>The children of the selected folder (or the roots when nothing is selected).</summary>
    protected IReadOnlyList<FileSystemEntry> Contents() => selected?.Children ?? Roots;

    /// <summary>The path from the root to the selected folder, root first.</summary>
    protected IReadOnlyList<FileSystemEntry> Breadcrumb()
    {
        List<FileSystemEntry> trail = new();
        FileSystemEntry? node = selected;
        while (node is not null)
        {
            trail.Add(node);
            node = parents.TryGetValue(node, out FileSystemEntry? parent) ? parent : null;
        }

        trail.Reverse();
        return trail;
    }

    protected Task ToggleFolderAsync(FileSystemEntry folder)
    {
        if (!expanded.Remove(folder))
        {
            expanded.Add(folder);
        }

        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>Selects a folder, expanding its ancestors so it is visible in the tree.</summary>
    protected Task SelectFolderAsync(FileSystemEntry folder)
    {
        selected = folder;
        for (FileSystemEntry? a = Ancestor(folder); a is not null; a = Ancestor(a))
        {
            expanded.Add(a);
        }

        StateHasChanged();
        return SelectedFolderChanged.HasDelegate
            ? SelectedFolderChanged.InvokeAsync(folder)
            : Task.CompletedTask;
    }

    /// <summary>Opens an entry: drills into folders, raises <see cref="OnFileOpen"/> for files.</summary>
    protected Task OpenEntryAsync(FileSystemEntry entry)
    {
        if (entry.IsDirectory)
        {
            expanded.Add(entry);
            return SelectFolderAsync(entry);
        }

        return OnFileOpen.HasDelegate ? OnFileOpen.InvokeAsync(entry) : Task.CompletedTask;
    }

    private FileSystemEntry? Ancestor(FileSystemEntry node) =>
        parents.TryGetValue(node, out FileSystemEntry? parent) ? parent : null;
}
