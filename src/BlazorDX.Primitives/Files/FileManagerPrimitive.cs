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

/// <summary>The outcome of a move request, for an <c>aria-live</c> announcement.</summary>
public readonly record struct MoveResult(FileSystemEntry Item, FileSystemEntry? Target, bool Succeeded);

/// <summary>
/// Tier 1 headless file manager. Maintains the expanded/selected folder state,
/// flattens the folder hierarchy for the navigation pane, exposes the selected
/// folder's contents and its breadcrumb path, and routes "open" actions (drill
/// into a folder, or raise a file-open event). Also owns a mutable working copy of
/// the tree so items can be <em>moved</em> between folders (drag-and-drop, or the
/// keyboard/single-pointer alternative) without mutating the immutable
/// <see cref="FileSystemEntry"/> records supplied via <see cref="Roots"/>. Renders
/// nothing itself.
/// </summary>
public class FileManagerPrimitive : ComponentBase
{
    private readonly HashSet<FileSystemEntry> expanded = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FileSystemEntry, FileSystemEntry?> parents =
        new(ReferenceEqualityComparer.Instance);

    // Mutable working children per folder, keyed by folder identity (root uses the
    // sentinel below). Seeded from the immutable model; moves edit these lists, so
    // the supplied records are never mutated.
    private readonly Dictionary<FileSystemEntry, List<FileSystemEntry>> children =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<FileSystemEntry> rootChildren = new();

    private FileSystemEntry? selected;
    private FileSystemEntry? moveCandidate;
    private object? lastRoots;

    [Parameter, EditorRequired] public IReadOnlyList<FileSystemEntry> Roots { get; set; } = [];

    /// <summary>Raised when a file (not a folder) is opened.</summary>
    [Parameter] public EventCallback<FileSystemEntry> OnFileOpen { get; set; }

    /// <summary>Raised when the selected folder changes.</summary>
    [Parameter] public EventCallback<FileSystemEntry?> SelectedFolderChanged { get; set; }

    /// <summary>Raised after an item is moved into a folder (or to the root when null).</summary>
    [Parameter] public EventCallback<MoveResult> OnItemMove { get; set; }

    protected FileSystemEntry? SelectedFolder => selected;

    /// <summary>Human-readable result of the last move/upload/delete, for an <c>aria-live</c> region.</summary>
    protected string StatusMessage { get; private set; } = string.Empty;

    /// <summary>The item currently marked to be moved by the keyboard/single-pointer path, if any.</summary>
    protected FileSystemEntry? MoveCandidate => moveCandidate;

    /// <summary>True when <paramref name="entry"/> is the item marked for moving.</summary>
    protected bool IsMoveCandidate(FileSystemEntry entry) => ReferenceEquals(moveCandidate, entry);

    protected override void OnParametersSet()
    {
        if (!ReferenceEqualityComparer.Instance.Equals(lastRoots, Roots))
        {
            lastRoots = Roots;
            Reindex();
        }
    }

    private void Reindex()
    {
        parents.Clear();
        children.Clear();
        rootChildren.Clear();
        rootChildren.AddRange(Roots);
        IndexParents(Roots, null);
    }

    private void IndexParents(IReadOnlyList<FileSystemEntry> nodes, FileSystemEntry? parent)
    {
        foreach (FileSystemEntry node in nodes)
        {
            parents[node] = parent;
            if (node.IsDirectory)
            {
                List<FileSystemEntry> kids = new(node.Children);
                children[node] = kids;
                IndexParents(node.Children, node);
            }
        }
    }

    // The current (mutable) children of a folder, or the roots when folder is null.
    private List<FileSystemEntry> ChildrenOf(FileSystemEntry? folder)
    {
        if (folder is null)
        {
            return rootChildren;
        }

        if (!children.TryGetValue(folder, out List<FileSystemEntry>? kids))
        {
            kids = new List<FileSystemEntry>(folder.Children);
            children[folder] = kids;
        }

        return kids;
    }

    protected bool IsExpanded(FileSystemEntry folder) => expanded.Contains(folder);

    protected bool IsSelected(FileSystemEntry folder) => ReferenceEquals(selected, folder);

    protected bool HasSubfolders(FileSystemEntry folder)
    {
        foreach (FileSystemEntry child in ChildrenOf(folder))
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
        AppendFolders(rootChildren, 0, rows);
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
                AppendFolders(ChildrenOf(node), depth + 1, rows);
            }
        }
    }

    /// <summary>The children of the selected folder (or the roots when nothing is selected).</summary>
    protected IReadOnlyList<FileSystemEntry> Contents() => ChildrenOf(selected);

    /// <summary>The folders an item could be moved into from the current contents view, in display order.</summary>
    protected IReadOnlyList<FileSystemEntry> MoveTargets()
    {
        List<FileSystemEntry> targets = new();
        foreach (FileSystemEntry entry in ChildrenOf(selected))
        {
            if (entry.IsDirectory)
            {
                targets.Add(entry);
            }
        }

        return targets;
    }

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

    /// <summary>
    /// Moves <paramref name="item"/> into <paramref name="target"/> (or to the root
    /// when <paramref name="target"/> is null). This is the single move primitive
    /// shared by native drag-and-drop and the keyboard/single-pointer "move" buttons
    /// (the WCAG 2.5.7 alternative). A move is rejected — and announced as such — when
    /// the target is the item itself, the item's current parent (a no-op), or a
    /// descendant of the item (which would orphan the subtree).
    /// </summary>
    /// <summary>
    /// Marks (or unmarks) an item as the one to move via the keyboard/single-pointer
    /// path: the first press arms the move, then activating a folder's "Move here"
    /// control places it. A second press on the same item cancels. This is the WCAG
    /// 2.5.7 "mark then place" alternative to the drag gesture — no pointer drag, no JS.
    /// </summary>
    protected Task ToggleMoveCandidateAsync(FileSystemEntry item)
    {
        if (ReferenceEquals(moveCandidate, item))
        {
            moveCandidate = null;
            StatusMessage = $"Move of {item.Name} cancelled.";
        }
        else
        {
            moveCandidate = item;
            StatusMessage = $"{item.Name} ready to move. Choose a destination folder, then select Move here.";
        }

        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>Places the currently-marked item into <paramref name="target"/> (root when null).</summary>
    protected async Task PlaceMoveCandidateAsync(FileSystemEntry? target)
    {
        if (moveCandidate is null)
        {
            return;
        }

        FileSystemEntry item = moveCandidate;
        moveCandidate = null;
        await MoveAsync(item, target);
    }

    protected async Task MoveAsync(FileSystemEntry item, FileSystemEntry? target)
    {
        bool ok = TryMove(item, target);
        StatusMessage = ok
            ? $"Moved {item.Name} to {target?.Name ?? "Files"}."
            : $"Could not move {item.Name}.";
        StateHasChanged();

        if (OnItemMove.HasDelegate)
        {
            await OnItemMove.InvokeAsync(new MoveResult(item, target, ok));
        }
    }

    private bool TryMove(FileSystemEntry item, FileSystemEntry? target)
    {
        FileSystemEntry? currentParent = parents.TryGetValue(item, out FileSystemEntry? p) ? p : null;

        if (ReferenceEquals(item, target) ||
            ReferenceEquals(currentParent, target) ||
            (target is not null && (!target.IsDirectory || IsDescendant(target, item))))
        {
            return false;
        }

        List<FileSystemEntry> from = ChildrenOf(currentParent);
        List<FileSystemEntry> to = ChildrenOf(target);

        int index = from.FindIndex(e => ReferenceEquals(e, item));
        if (index < 0)
        {
            return false;
        }

        from.RemoveAt(index);
        to.Add(item);
        parents[item] = target;
        if (target is not null)
        {
            expanded.Add(target);
        }

        return true;
    }

    // True when candidate is item itself or sits anywhere beneath item in the tree.
    private bool IsDescendant(FileSystemEntry candidate, FileSystemEntry item)
    {
        for (FileSystemEntry? n = candidate; n is not null; n = Ancestor(n))
        {
            if (ReferenceEquals(n, item))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Records an upload result for the <c>aria-live</c> region (4.1.3).</summary>
    protected void AnnounceUpload(int count, FileSystemEntry? destination)
    {
        string where = destination?.Name ?? "Files";
        StatusMessage = count == 1
            ? $"Uploaded 1 file to {where}."
            : $"Uploaded {count} files to {where}.";
        StateHasChanged();
    }

    private FileSystemEntry? Ancestor(FileSystemEntry node) =>
        parents.TryGetValue(node, out FileSystemEntry? parent) ? parent : null;
}
