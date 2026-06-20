using System.Globalization;
using BlazorDX.Primitives.Files;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled file manager built on <see cref="FileManagerPrimitive"/>: a
/// breadcrumb, a folder navigation tree on the left, and the selected folder's
/// contents on the right. Double-click a folder to drill in, or a file to open it.
/// Styling is token-driven (see dx-filemanager.css).
/// </summary>
public sealed class DxFileManager : FileManagerPrimitive
{
    private const int IndentPerLevel = 16;

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-fm {Class}".TrimEnd());

        BuildBreadcrumb(builder);

        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-fm-panes");

        BuildTree(builder);
        BuildContents(builder);

        builder.CloseElement();
        builder.CloseElement();
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

        foreach (FileSystemEntry crumb in Breadcrumb())
        {
            FileSystemEntry captured = crumb;
            builder.OpenElement(10, "span");
            builder.AddAttribute(11, "class", "dx-fm-sep");
            builder.AddAttribute(12, "aria-hidden", "true");
            builder.AddContent(13, "›");
            builder.CloseElement();

            builder.OpenElement(14, "button");
            builder.SetKey(crumb);
            builder.AddAttribute(15, "type", "button");
            builder.AddAttribute(16, "class", "dx-fm-crumb");
            builder.AddAttribute(17, "onclick", EventCallback.Factory.Create(this, () => SelectFolderAsync(captured)));
            builder.AddContent(18, crumb.Name);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(19, "div");
        builder.AddAttribute(20, "class", "dx-fm-tree");
        builder.AddAttribute(21, "role", "tree");

        foreach (FolderRow row in FolderRows())
        {
            FileSystemEntry folder = row.Entry;
            builder.OpenElement(22, "div");
            builder.SetKey(folder);
            builder.AddAttribute(23, "class", row.Selected ? "dx-fm-node dx-fm-node-selected" : "dx-fm-node");
            builder.AddAttribute(24, "role", "treeitem");
            builder.AddAttribute(25, "aria-level", row.Depth + 1);
            if (row.HasSubfolders)
            {
                builder.AddAttribute(26, "aria-expanded", row.Expanded ? "true" : "false");
            }

            builder.AddAttribute(27, "aria-selected", row.Selected ? "true" : "false");
            builder.AddAttribute(28, "style",
                string.Create(CultureInfo.InvariantCulture, $"padding-left:{row.Depth * IndentPerLevel}px;"));

            if (row.HasSubfolders)
            {
                builder.OpenElement(29, "button");
                builder.AddAttribute(30, "type", "button");
                builder.AddAttribute(31, "class", "dx-fm-twisty");
                builder.AddAttribute(32, "aria-label", row.Expanded ? "Collapse" : "Expand");
                builder.AddAttribute(33, "onclick", EventCallback.Factory.Create(this, () => ToggleFolderAsync(folder)));
                builder.AddContent(34, row.Expanded ? "▾" : "▸");
                builder.CloseElement();
            }
            else
            {
                builder.OpenElement(35, "span");
                builder.AddAttribute(36, "class", "dx-fm-twisty-spacer");
                builder.CloseElement();
            }

            builder.OpenElement(37, "button");
            builder.AddAttribute(38, "type", "button");
            builder.AddAttribute(39, "class", "dx-fm-node-label");
            builder.AddAttribute(40, "onclick", EventCallback.Factory.Create(this, () => SelectFolderAsync(folder)));
            builder.AddContent(41, "📁");
            builder.AddContent(42, folder.Name);
            builder.CloseElement();

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildContents(RenderTreeBuilder builder)
    {
        builder.OpenElement(43, "div");
        builder.AddAttribute(44, "class", "dx-fm-contents");
        builder.AddAttribute(45, "role", "table");
        builder.AddAttribute(46, "aria-label", "Folder contents");

        builder.OpenElement(47, "div");
        builder.AddAttribute(48, "class", "dx-fm-content-head");
        Header(builder, 49, "Name");
        Header(builder, 50, "Size");
        Header(builder, 51, "Modified");
        builder.CloseElement();

        IReadOnlyList<FileSystemEntry> contents = Contents();
        if (contents.Count == 0)
        {
            builder.OpenElement(52, "div");
            builder.AddAttribute(53, "class", "dx-fm-empty");
            builder.AddContent(54, "This folder is empty.");
            builder.CloseElement();
        }

        foreach (FileSystemEntry entry in contents)
        {
            FileSystemEntry captured = entry;
            builder.OpenElement(55, "div");
            builder.SetKey(entry);
            builder.AddAttribute(56, "class", "dx-fm-content-row");
            builder.AddAttribute(57, "role", "row");
            builder.AddAttribute(58, "tabindex", "0");
            builder.AddAttribute(59, "ondblclick", EventCallback.Factory.Create(this, () => OpenEntryAsync(captured)));

            builder.OpenElement(60, "span");
            builder.AddAttribute(61, "class", "dx-fm-cell dx-fm-name");
            builder.AddContent(62, entry.IsDirectory ? "📁" : "📄");
            builder.AddContent(63, entry.Name);
            builder.CloseElement();

            builder.OpenElement(64, "span");
            builder.AddAttribute(65, "class", "dx-fm-cell dx-fm-size");
            builder.AddContent(66, entry.IsDirectory ? "—" : FormatSize(entry.Size));
            builder.CloseElement();

            builder.OpenElement(67, "span");
            builder.AddAttribute(68, "class", "dx-fm-cell dx-fm-modified");
            builder.AddContent(69, entry.Modified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            builder.CloseElement();

            builder.CloseElement();
        }

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
