using System.Globalization;
using BlazorDX.Interop;
using BlazorDX.Primitives.Files;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled file manager built on <see cref="FileManagerPrimitive"/>: a
/// breadcrumb, a folder navigation tree on the left, and the selected folder's
/// contents on the right. Double-click a folder to drill in, or a file to open it.
///
/// <para>Drag-and-drop is a hybrid enhancement layered on top, never the only path:
/// items can be dragged to move them within or between panes, and OS files can be
/// dropped onto the contents pane to upload — both via a native HTML5 DnD bridge
/// (<see cref="IFileDndInterop"/>). The always-available equivalents are a
/// keyboard/single-pointer "mark then place" move (WCAG 2.5.7) and a standard
/// <see cref="InputFile"/> upload control. Move/upload outcomes are announced through
/// an <c>aria-live</c> region (4.1.3). Styling is token-driven (see dx-filemanager.css).</para>
/// </summary>
public sealed class DxFileManager : FileManagerPrimitive, IAsyncDisposable
{
    private const int IndentPerLevel = 16;

    [Inject] private IFileDndInterop Dnd { get; set; } = default!;

    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Raised when OS files are dropped on the contents pane (native DnD). Carries the
    /// dropped files' metadata and the destination folder (null = root). Streaming the
    /// bytes is the host's job; the always-available <see cref="InputFile"/> path uses
    /// <see cref="OnUpload"/> with real <see cref="IBrowserFile"/> streams instead.
    /// </summary>
    [Parameter] public EventCallback<FileDropArgs> OnFilesDropped { get; set; }

    /// <summary>Raised when files are chosen through the always-available upload control.</summary>
    [Parameter] public EventCallback<IReadOnlyList<IBrowserFile>> OnUpload { get; set; }

    // Stable element-id base for this instance, so JS DnD addresses elements by id
    // (no ElementReference crosses the boundary). Unique per component instance.
    private readonly string baseId = $"dxfm-{Guid.NewGuid():N}";

    // The contents shown in the last render, in row order, so a JS drop callback that
    // reports a row element id can resolve it back to the entry it represents.
    private IReadOnlyList<FileSystemEntry> renderedRows = [];
    private IReadOnlyList<FolderRow> renderedNodes = [];
    private readonly HashSet<string> registered = new(StringComparer.Ordinal);
    private bool disposed;

    private string RowId(int index) => $"{baseId}-row-{index}";

    private string NodeId(int index) => $"{baseId}-node-{index}";

    private string DropZoneId => $"{baseId}-drop";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-fm {Class}".TrimEnd());

        BuildBreadcrumb(builder);
        BuildToolbar(builder);

        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-fm-panes");

        BuildTree(builder);
        BuildContents(builder);

        builder.CloseElement();   // panes

        BuildStatus(builder);

        builder.CloseElement();   // root
    }

    private void BuildBreadcrumb(RenderTreeBuilder builder)
    {
        builder.OpenElement(4, "nav");
        builder.AddAttribute(5, "class", "dx-fm-breadcrumb");
        builder.AddAttribute(6, "aria-label", "Path");

        builder.OpenElement(7, "span");
        builder.AddAttribute(8, "class", "dx-fm-crumb-home");
        builder.AddContent(9, "Files");
        builder.CloseElement();

        // "Move here" affordance on the root while an item is marked for moving.
        if (MoveCandidate is not null)
        {
            builder.OpenElement(10, "button");
            builder.AddAttribute(11, "type", "button");
            builder.AddAttribute(12, "class", "dx-fm-move-here");
            builder.AddAttribute(13, "aria-label", $"Move {MoveCandidate.Name} to Files");
            builder.AddAttribute(14, "onclick", EventCallback.Factory.Create(this, () => PlaceMoveCandidateAsync(null)));
            builder.AddContent(15, "Move here");
            builder.CloseElement();
        }

        foreach (FileSystemEntry crumb in Breadcrumb())
        {
            FileSystemEntry captured = crumb;
            builder.OpenElement(16, "span");
            builder.AddAttribute(17, "class", "dx-fm-sep");
            builder.AddAttribute(18, "aria-hidden", "true");
            builder.AddContent(19, "›");
            builder.CloseElement();

            builder.OpenElement(20, "button");
            builder.SetKey(crumb);
            builder.AddAttribute(21, "type", "button");
            builder.AddAttribute(22, "class", "dx-fm-crumb");
            builder.AddAttribute(23, "onclick", EventCallback.Factory.Create(this, () => SelectFolderAsync(captured)));
            builder.AddContent(24, crumb.Name);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildToolbar(RenderTreeBuilder builder)
    {
        builder.OpenElement(25, "div");
        builder.AddAttribute(26, "class", "dx-fm-toolbar");

        // Always-available upload path (no drag required). The label wraps the
        // InputFile so the whole control is clickable and keyboard-reachable.
        builder.OpenElement(27, "label");
        builder.AddAttribute(28, "class", "dx-fm-upload");

        builder.OpenComponent<InputFile>(29);
        builder.AddComponentParameter(30, "class", "dx-fm-upload-input");
        builder.AddComponentParameter(31, "multiple", true);
        builder.AddComponentParameter(32, "OnChange",
            EventCallback.Factory.Create<InputFileChangeEventArgs>(this, HandleUploadAsync));
        builder.CloseComponent();

        builder.OpenElement(33, "span");
        builder.AddAttribute(34, "class", "dx-fm-upload-prompt");
        builder.AddContent(35, "Upload files");
        builder.CloseElement();

        builder.CloseElement();   // label
        builder.CloseElement();   // toolbar
    }

    private void BuildTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(36, "div");
        builder.AddAttribute(37, "class", "dx-fm-tree");
        builder.AddAttribute(38, "role", "tree");

        IReadOnlyList<FolderRow> rows = FolderRows();
        renderedNodes = rows;

        for (int i = 0; i < rows.Count; i++)
        {
            FolderRow row = rows[i];
            FileSystemEntry folder = row.Entry;
            int nodeIndex = i;

            builder.OpenElement(39, "div");
            builder.SetKey(folder);
            builder.AddAttribute(40, "id", NodeId(nodeIndex));
            builder.AddAttribute(41, "class", row.Selected ? "dx-fm-node dx-fm-node-selected" : "dx-fm-node");
            builder.AddAttribute(42, "role", "treeitem");
            builder.AddAttribute(43, "aria-level", row.Depth + 1);
            if (row.HasSubfolders)
            {
                builder.AddAttribute(44, "aria-expanded", row.Expanded ? "true" : "false");
            }

            builder.AddAttribute(45, "aria-selected", row.Selected ? "true" : "false");
            builder.AddAttribute(46, "style",
                string.Create(CultureInfo.InvariantCulture, $"padding-left:{row.Depth * IndentPerLevel}px;"));

            if (row.HasSubfolders)
            {
                builder.OpenElement(47, "button");
                builder.AddAttribute(48, "type", "button");
                builder.AddAttribute(49, "class", "dx-fm-twisty");
                builder.AddAttribute(50, "aria-label", row.Expanded ? "Collapse" : "Expand");
                builder.AddAttribute(51, "onclick", EventCallback.Factory.Create(this, () => ToggleFolderAsync(folder)));
                builder.AddContent(52, row.Expanded ? "▾" : "▸");
                builder.CloseElement();
            }
            else
            {
                builder.OpenElement(53, "span");
                builder.AddAttribute(54, "class", "dx-fm-twisty-spacer");
                builder.CloseElement();
            }

            builder.OpenElement(55, "button");
            builder.AddAttribute(56, "type", "button");
            builder.AddAttribute(57, "class", "dx-fm-node-label");
            builder.AddAttribute(58, "onclick", EventCallback.Factory.Create(this, () => SelectFolderAsync(folder)));
            builder.AddContent(59, "📁");
            builder.AddContent(60, folder.Name);
            builder.CloseElement();

            // "Move here" target for the keyboard/single-pointer path: visible only
            // while an item is marked, and never on the marked item's own subtree.
            if (MoveCandidate is not null && !ReferenceEquals(MoveCandidate, folder))
            {
                builder.OpenElement(61, "button");
                builder.AddAttribute(62, "type", "button");
                builder.AddAttribute(63, "class", "dx-fm-move-here");
                builder.AddAttribute(64, "aria-label", $"Move {MoveCandidate.Name} into {folder.Name}");
                builder.AddAttribute(65, "onclick", EventCallback.Factory.Create(this, () => PlaceMoveCandidateAsync(folder)));
                builder.AddContent(66, "Move here");
                builder.CloseElement();
            }

            builder.CloseElement();   // node
        }

        builder.CloseElement();
    }

    private void BuildContents(RenderTreeBuilder builder)
    {
        builder.OpenElement(67, "div");
        builder.AddAttribute(68, "id", DropZoneId);
        builder.AddAttribute(69, "class", "dx-fm-contents");
        builder.AddAttribute(70, "role", "table");
        builder.AddAttribute(71, "aria-label", "Folder contents. Drop files here to upload.");

        builder.OpenElement(72, "div");
        builder.AddAttribute(73, "class", "dx-fm-content-head");
        Header(builder, 74, "Name");
        Header(builder, 78, "Size");
        Header(builder, 82, "Modified");
        Header(builder, 86, "Actions");
        builder.CloseElement();

        IReadOnlyList<FileSystemEntry> contents = Contents();
        renderedRows = contents;

        if (contents.Count == 0)
        {
            builder.OpenElement(90, "div");
            builder.AddAttribute(91, "class", "dx-fm-empty");
            builder.AddContent(92, "This folder is empty. Drop files here or use Upload files.");
            builder.CloseElement();
        }

        for (int i = 0; i < contents.Count; i++)
        {
            FileSystemEntry entry = contents[i];
            int rowIndex = i;
            bool marked = IsMoveCandidate(entry);

            builder.OpenElement(93, "div");
            builder.SetKey(entry);
            builder.AddAttribute(94, "id", RowId(rowIndex));
            builder.AddAttribute(95, "class", marked ? "dx-fm-content-row dx-fm-row-moving" : "dx-fm-content-row");
            builder.AddAttribute(96, "role", "row");
            builder.AddAttribute(97, "tabindex", "0");
            builder.AddAttribute(98, "ondblclick", EventCallback.Factory.Create(this, () => OpenEntryAsync(entry)));

            builder.OpenElement(99, "span");
            builder.AddAttribute(100, "class", "dx-fm-cell dx-fm-name");
            builder.AddContent(101, entry.IsDirectory ? "📁" : "📄");
            builder.AddContent(102, entry.Name);
            builder.CloseElement();

            builder.OpenElement(103, "span");
            builder.AddAttribute(104, "class", "dx-fm-cell dx-fm-size");
            builder.AddContent(105, entry.IsDirectory ? "—" : FormatSize(entry.Size));
            builder.CloseElement();

            builder.OpenElement(106, "span");
            builder.AddAttribute(107, "class", "dx-fm-cell dx-fm-modified");
            builder.AddContent(108, entry.Modified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            builder.CloseElement();

            // Per-row "Move" toggle: arms the keyboard/single-pointer move (2.5.7).
            // The button is the 24×24 action target; toggling it sets aria-pressed.
            builder.OpenElement(109, "span");
            builder.AddAttribute(110, "class", "dx-fm-cell dx-fm-actions");

            builder.OpenElement(111, "button");
            builder.AddAttribute(112, "type", "button");
            builder.AddAttribute(113, "class", "dx-fm-action");
            builder.AddAttribute(114, "aria-pressed", marked ? "true" : "false");
            builder.AddAttribute(115, "aria-label", marked ? $"Cancel moving {entry.Name}" : $"Move {entry.Name}");
            builder.AddAttribute(116, "onclick", EventCallback.Factory.Create(this, () => ToggleMoveCandidateAsync(entry)));
            builder.AddContent(117, marked ? "✕" : "↔");
            builder.CloseElement();

            builder.CloseElement();   // actions cell
            builder.CloseElement();   // row
        }

        builder.CloseElement();
    }

    private void BuildStatus(RenderTreeBuilder builder)
    {
        // aria-live region: announces move/upload results asynchronously (WCAG 4.1.3).
        builder.OpenElement(118, "div");
        builder.AddAttribute(119, "class", "dx-fm-status");
        builder.AddAttribute(120, "role", "status");
        builder.AddAttribute(121, "aria-live", "polite");
        builder.AddContent(122, StatusMessage);
        builder.CloseElement();
    }

    private static void Header(RenderTreeBuilder builder, int seq, string text)
    {
        builder.OpenElement(seq, "span");
        builder.AddAttribute(seq + 1, "class", "dx-fm-cell");
        builder.AddAttribute(seq + 2, "role", "columnheader");
        builder.AddContent(seq + 3, text);
        builder.CloseElement();
    }

    private async Task HandleUploadAsync(InputFileChangeEventArgs args)
    {
        IReadOnlyList<IBrowserFile> files = args.GetMultipleFiles();
        AnnounceUpload(files.Count, SelectedFolder);
        if (OnUpload.HasDelegate)
        {
            await OnUpload.InvokeAsync(files);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await Dnd.EnsureLoadedAsync();

        // (Re)wire native DnD against the ids rendered this pass. The TS register*
        // calls are idempotent (they replace prior wiring for the same id), so calling
        // them every render keeps the DOM and the bridge in sync as rows change.
        HashSet<string> current = new(StringComparer.Ordinal);

        for (int i = 0; i < renderedRows.Count; i++)
        {
            FileSystemEntry entry = renderedRows[i];
            string id = RowId(i);
            current.Add(id);
            await Dnd.RegisterDraggableAsync(id);
            if (entry.IsDirectory)
            {
                int captured = i;
                await Dnd.RegisterDropTargetAsync(id, (s, _) => OnDomMove(s, captured), OnDomFilesRow(captured));
            }
        }

        for (int i = 0; i < renderedNodes.Count; i++)
        {
            string id = NodeId(i);
            current.Add(id);
            int captured = i;
            await Dnd.RegisterDropTargetAsync(id, (s, _) => OnDomMoveToNode(s, captured), NoFiles);
        }

        // The contents pane is the OS-file drop zone (upload), and a move target to root.
        current.Add(DropZoneId);
        await Dnd.RegisterDropTargetAsync(DropZoneId, (s, _) => OnDomMoveToRoot(s), OnDomFilesToSelected);

        foreach (string stale in registered)
        {
            if (!current.Contains(stale))
            {
                await Dnd.UnregisterAsync(stale);
            }
        }

        registered.Clear();
        foreach (string id in current)
        {
            registered.Add(id);
        }
    }

    // Resolves a dragged row id back to its entry and moves it into the target folder.
    private void OnDomMove(string sourceId, int targetRowIndex)
    {
        FileSystemEntry? source = ResolveRow(sourceId);
        if (source is not null && targetRowIndex < renderedRows.Count)
        {
            _ = MoveAsync(source, renderedRows[targetRowIndex]);
        }
    }

    private void OnDomMoveToNode(string sourceId, int nodeIndex)
    {
        FileSystemEntry? source = ResolveRow(sourceId);
        if (source is not null && nodeIndex < renderedNodes.Count)
        {
            _ = MoveAsync(source, renderedNodes[nodeIndex].Entry);
        }
    }

    private void OnDomMoveToRoot(string sourceId)
    {
        FileSystemEntry? source = ResolveRow(sourceId);
        if (source is not null)
        {
            _ = MoveAsync(source, SelectedFolder);
        }
    }

    private FileSystemEntry? ResolveRow(string elementId)
    {
        string prefix = $"{baseId}-row-";
        if (!elementId.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(elementId.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
            index >= renderedRows.Count)
        {
            return null;
        }

        return renderedRows[index];
    }

    private Action<IReadOnlyList<DroppedFile>> OnDomFilesRow(int targetRowIndex) => files =>
    {
        FileSystemEntry? target = targetRowIndex < renderedRows.Count ? renderedRows[targetRowIndex] : null;
        RaiseDrop(files, target?.IsDirectory == true ? target : SelectedFolder);
    };

    private void OnDomFilesToSelected(IReadOnlyList<DroppedFile> files) => RaiseDrop(files, SelectedFolder);

    private void RaiseDrop(IReadOnlyList<DroppedFile> files, FileSystemEntry? destination)
    {
        if (files.Count == 0)
        {
            return;
        }

        AnnounceUpload(files.Count, destination);
        if (OnFilesDropped.HasDelegate)
        {
            _ = OnFilesDropped.InvokeAsync(new FileDropArgs(files, destination));
        }
    }

    private static void NoFiles(IReadOnlyList<DroppedFile> files)
    {
        // Tree nodes accept item moves only, not OS-file uploads.
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (string id in registered)
        {
            await Dnd.UnregisterAsync(id);
        }

        await Dnd.DisposeAsync();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double kb = bytes / 1024.0;
        if (kb < 1024)
        {
            return $"{kb:0.#} KB";
        }

        return $"{kb / 1024:0.#} MB";
    }
}

/// <summary>OS files dropped on the file manager via native drag-and-drop, plus their destination.</summary>
public readonly record struct FileDropArgs(IReadOnlyList<DroppedFile> Files, FileSystemEntry? Destination);
