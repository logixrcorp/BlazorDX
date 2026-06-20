using System.Globalization;
using BlazorDX.Compute;
using BlazorDX.Primitives.Grid;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled data grid. It inherits all behavior from
/// <see cref="DataGridPrimitive{TRow}"/> and only supplies markup, built with
/// <see cref="RenderTreeBuilder"/> and hardcoded sequence numbers so Blazor's
/// linear-time diff stays intact across thousands of cells. Styling is class- and
/// CSS-variable-based (see dx-datagrid.css); nothing here imposes a design system.
/// </summary>
/// <typeparam name="TRow">The row type, bound via a generated accessor.</typeparam>
public sealed class DxDataGrid<TRow> : DataGridPrimitive<TRow>
{
    /// <summary>Optional extra CSS classes appended to the grid container.</summary>
    [Parameter] public string? Class { get; set; }

    private int dragColumn = -1;
    private bool chooserOpen;
    private int openMenuColumn = -1;

    // Row selection is an in-memory feature (selection is keyed by absolute index, which
    // is meaningless once the server re-sorts), so it is suppressed in remote mode.
    private bool ShowSelection => Selectable && !RemoteMode;

    private void BuildColumnChooser(RenderTreeBuilder builder)
    {
        builder.OpenElement(140, "div");
        builder.AddAttribute(141, "class", "dx-grid-chooser");

        builder.OpenElement(142, "button");
        builder.AddAttribute(143, "type", "button");
        builder.AddAttribute(144, "class", "dx-grid-chooser-toggle");
        builder.AddAttribute(145, "aria-expanded", chooserOpen ? "true" : "false");
        builder.AddAttribute(146, "onclick", EventCallback.Factory.Create(this, () => chooserOpen = !chooserOpen));
        builder.AddContent(147, $"Columns ({VisibleColumnCount}/{Columns.Count}) ▾");
        builder.CloseElement();

        if (chooserOpen)
        {
            builder.OpenElement(148, "div");
            builder.AddAttribute(149, "class", "dx-grid-chooser-panel");
            builder.AddAttribute(150, "role", "group");

            for (int display = 0; display < Columns.Count; display++)
            {
                int actual = ColumnOrder[display];
                bool visible = !IsColumnHidden(actual);
                bool lastVisible = visible && VisibleColumnCount == 1;   // keep at least one column

                builder.OpenElement(151, "label");
                builder.SetKey(actual);
                builder.AddAttribute(152, "class", "dx-grid-chooser-item");

                builder.OpenElement(153, "input");
                builder.AddAttribute(154, "type", "checkbox");
                builder.AddAttribute(155, "checked", visible);
                builder.AddAttribute(156, "disabled", lastVisible);
                builder.AddAttribute(157, "onchange", EventCallback.Factory.Create(this, () => ToggleColumnVisibility(actual)));
                builder.CloseElement();

                builder.AddContent(158, Columns[actual].Header);
                builder.CloseElement();
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        string columnTemplate = BuildColumnTemplate();

        if (ShowExport || ShowExcelExport || ShowPdfExport || ShowColumnChooser || ShowClipboard)
        {
            builder.OpenElement(130, "div");
            builder.AddAttribute(131, "class", "dx-grid-toolbar");

            if (ShowColumnChooser)
            {
                BuildColumnChooser(builder);
            }

            if (ShowClipboard)
            {
                builder.OpenElement(137, "button");
                builder.AddAttribute(138, "type", "button");
                builder.AddAttribute(139, "class", "dx-grid-export");
                builder.AddAttribute(190, "onclick", EventCallback.Factory.Create(this, CopySelectionAsync));
                builder.AddContent(191, "⧉ Copy");
                builder.CloseElement();
            }

            if (ShowExport)
            {
                builder.OpenElement(132, "button");
                builder.AddAttribute(133, "type", "button");
                builder.AddAttribute(134, "class", "dx-grid-export");
                builder.AddAttribute(135, "onclick", EventCallback.Factory.Create(this, ExportCsvAsync));
                builder.AddContent(136, "⭳ Export CSV");
                builder.CloseElement();
            }

            if (ShowExcelExport)
            {
                builder.OpenElement(192, "button");
                builder.AddAttribute(193, "type", "button");
                builder.AddAttribute(194, "class", "dx-grid-export");
                builder.AddAttribute(195, "onclick", EventCallback.Factory.Create(this, ExportXlsxAsync));
                builder.AddContent(196, "⭳ Export Excel");
                builder.CloseElement();
            }

            if (ShowPdfExport)
            {
                builder.OpenElement(197, "button");
                builder.AddAttribute(198, "type", "button");
                builder.AddAttribute(199, "class", "dx-grid-export");
                builder.AddAttribute(200, "onclick", EventCallback.Factory.Create(this, ExportPdfAsync));
                builder.AddContent(201, "⭳ Export PDF");
                builder.CloseElement();
            }

            builder.CloseElement();
        }

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", ContainerId);
        builder.AddAttribute(2, "class", $"dx-grid{(HasPinnedColumns ? " dx-grid-pinnable" : string.Empty)} {Class}".TrimEnd());
        builder.AddAttribute(3, "role", "grid");
        builder.AddAttribute(4, "aria-rowcount", TotalRows);
        builder.AddAttribute(5, "style", $"height:{ViewportHeight}px;");

        // Keyboard cell navigation: the grid container (role=grid) is the tab stop and
        // owns aria-activedescendant (valid here; it is not allowed on role=rowgroup).
        if (KeyboardNavigation)
        {
            builder.AddAttribute(210, "tabindex", "0");
            if (HasActiveCell)
            {
                builder.AddAttribute(211, "aria-activedescendant", ActiveCellId);
            }

            builder.AddAttribute(212, "onkeydown",
                EventCallback.Factory.Create<KeyboardEventArgs>(this, OnCellNavigationAsync));
        }

        BuildHeader(builder, columnTemplate);
        if (Filterable)
        {
            BuildFilterRow(builder, columnTemplate);
        }

        BuildBody(builder, columnTemplate);
        if (HasAggregations)
        {
            BuildFooter(builder, columnTemplate);
        }

        // While resizing, a full-window overlay captures pointer move/up so the
        // gesture keeps tracking even if the cursor leaves the thin handle.
        if (IsResizing)
        {
            builder.OpenElement(120, "div");
            builder.AddAttribute(121, "class", "dx-grid-resize-overlay");
            builder.AddAttribute(122, "onpointermove",
                EventCallback.Factory.Create<PointerEventArgs>(this, e => ResizeColumnTo(e.ClientX)));
            builder.AddAttribute(123, "onpointerup", EventCallback.Factory.Create(this, EndColumnResize));
            builder.AddAttribute(124, "onpointerleave", EventCallback.Factory.Create(this, EndColumnResize));
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    // Builds the CSS grid track list: a leading selection column (when enabled)
    // plus one track per data column — an explicit px width if resized, else 1fr.
    private string BuildColumnTemplate()
    {
        System.Text.StringBuilder tracks = new("display:grid;grid-template-columns:");
        if (ShowSelection)
        {
            tracks.Append("36px ");
        }

        for (int display = 0; display < Columns.Count; display++)
        {
            int actual = ColumnOrder[display];
            if (IsColumnHidden(actual))
            {
                continue;   // hidden columns contribute no grid track
            }

            // Pinned columns need a fixed width so their sticky offsets line up.
            if (IsPinned(display))
            {
                tracks.Append(string.Create(CultureInfo.InvariantCulture, $"{PinnedColumnWidth(actual):0}px "));
            }
            else
            {
                double width = ColumnWidth(actual);
                tracks.Append(width > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"{width:0}px ")
                    : "minmax(80px,1fr) ");
            }
        }

        tracks.Append(';');
        return tracks.ToString();
    }

    // Sticky positioning for a frozen column at the given display position (else empty).
    private string PinStyle(int displayPosition) =>
        IsPinned(displayPosition)
            ? string.Create(CultureInfo.InvariantCulture, $"position:sticky;left:{PinnedLeft(displayPosition):0.#}px;")
            : string.Empty;

    private string PinClass(int displayPosition)
    {
        if (!IsPinned(displayPosition))
        {
            return string.Empty;
        }

        return IsLastPinned(displayPosition) ? " dx-grid-pinned dx-grid-pin-edge" : " dx-grid-pinned";
    }

    // The leading selection column pins to the very left when any columns are frozen.
    private string SelectPinStyle() => ShowSelection && HasPinnedColumns ? "position:sticky;left:0;" : string.Empty;

    private string SelectPinClass() => ShowSelection && HasPinnedColumns ? " dx-grid-pinned" : string.Empty;

    private void BuildFilterRow(RenderTreeBuilder builder, string columnTemplate)
    {
        builder.OpenElement(60, "div");
        builder.AddAttribute(61, "class", "dx-grid-filters");
        builder.AddAttribute(62, "role", "row");
        builder.AddAttribute(63, "style", columnTemplate);

        if (ShowSelection)
        {
            builder.OpenElement(64, "div");
            builder.AddAttribute(65, "class", $"dx-grid-filter-cell dx-grid-select-cell{SelectPinClass()}");
            builder.AddAttribute(66, "style", SelectPinStyle());
            builder.CloseElement();
        }

        for (int display = 0; display < Columns.Count; display++)
        {
            int actual = ColumnOrder[display];
            if (IsColumnHidden(actual))
            {
                continue;
            }

            builder.OpenElement(66, "div");
            builder.AddAttribute(67, "class", $"dx-grid-filter-cell{PinClass(display)}");
            builder.AddAttribute(68, "role", "gridcell");
            builder.AddAttribute(78, "style", PinStyle(display));

            builder.OpenElement(69, "input");
            builder.AddAttribute(70, "class", "dx-grid-filter-input");
            builder.AddAttribute(71, "type", "text");
            builder.AddAttribute(72, "placeholder", "Filter…");
            builder.AddAttribute(73, "aria-label", $"Filter {Columns[actual].Header}");
            builder.AddAttribute(74, "value", ColumnFilter(actual));
            builder.AddAttribute(75, "oninput",
                EventCallback.Factory.Create<ChangeEventArgs>(this, e => SetFilter(actual, e.Value as string)));
            // Keep arrow keys in the filter from bubbling to the grid's cell navigation.
            builder.AddEventStopPropagationAttribute(76, "onkeydown", true);
            builder.CloseElement();

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildFooter(RenderTreeBuilder builder, string columnTemplate)
    {
        builder.OpenElement(32, "div");
        builder.AddAttribute(33, "class", "dx-grid-footer");
        builder.AddAttribute(34, "role", "row");
        builder.AddAttribute(35, "style", columnTemplate);

        if (ShowSelection)
        {
            builder.OpenElement(95, "div");
            builder.AddAttribute(96, "class", $"dx-grid-foot-cell dx-grid-select-cell{SelectPinClass()}");
            builder.AddAttribute(97, "style", SelectPinStyle());
            builder.CloseElement();
        }

        for (int display = 0; display < Columns.Count; display++)
        {
            int actual = ColumnOrder[display];
            if (IsColumnHidden(actual))
            {
                continue;
            }

            builder.OpenElement(36, "div");
            builder.AddAttribute(37, "class", $"dx-grid-foot-cell{PinClass(display)}");
            builder.AddAttribute(38, "role", "gridcell");
            builder.AddAttribute(98, "style", PinStyle(display));
            if (AggregationKind(actual) is GridAggregateKind kind)
            {
                builder.AddContent(39, FormatAggregate(kind, AggregateFor(actual)));
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static string FormatAggregate(GridAggregateKind kind, GridAggregate aggregate)
    {
        double value = aggregate.Value(kind);
        if (double.IsNaN(value))
        {
            return string.Empty;
        }

        string label = kind switch
        {
            GridAggregateKind.Sum => "Σ",
            GridAggregateKind.Mean => "x̄",
            GridAggregateKind.Min => "min",
            GridAggregateKind.Max => "max",
            GridAggregateKind.Count => "n",
            _ => string.Empty,
        };

        string formatted = kind == GridAggregateKind.Count
            ? value.ToString("N0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

        return $"{label} {formatted}";
    }

    private void BuildHeader(RenderTreeBuilder builder, string columnTemplate)
    {
        builder.OpenElement(6, "div");
        builder.AddAttribute(7, "class", "dx-grid-header");
        builder.AddAttribute(8, "role", "row");
        builder.AddAttribute(9, "style", columnTemplate);

        if (ShowSelection)
        {
            builder.OpenElement(76, "div");
            builder.AddAttribute(77, "class", $"dx-grid-th dx-grid-select-cell{SelectPinClass()}");
            builder.AddAttribute(78, "role", "columnheader");
            builder.AddAttribute(99, "style", SelectPinStyle());

            builder.OpenElement(79, "input");
            builder.AddAttribute(80, "type", "checkbox");
            builder.AddAttribute(81, "class", "dx-grid-select");
            builder.AddAttribute(82, "aria-label", "Select all rows");
            builder.AddAttribute(83, "checked", AllVisibleSelected);
            builder.AddAttribute(84, "indeterminate", SomeVisibleSelected);
            builder.AddAttribute(85, "onchange", EventCallback.Factory.Create(this, ToggleSelectAll));
            builder.CloseElement();

            builder.CloseElement();
        }

        for (int display = 0; display < Columns.Count; display++)
        {
            int actual = ColumnOrder[display];
            if (IsColumnHidden(actual))
            {
                continue;
            }

            int capturedDisplay = display;
            GridColumnInfo column = Columns[actual];

            // No SetKey: keep header nodes position-stable so reordering updates
            // content in place rather than moving keyed nodes.
            builder.OpenElement(10, "div");
            builder.AddAttribute(11, "class", $"dx-grid-th{PinClass(capturedDisplay)}");
            builder.AddAttribute(12, "style", PinStyle(capturedDisplay));
            builder.AddAttribute(13, "role", "columnheader");
            builder.AddAttribute(14, "aria-sort", AriaSortFor(actual));
            // Don't make the header draggable mid-resize, or the browser starts a drag.
            builder.AddAttribute(15, "draggable", IsResizing ? "false" : "true");
            builder.AddAttribute(16, "title", "Drag, or Ctrl+Arrow, to reorder");
            builder.AddAttribute(18, "ondragstart", EventCallback.Factory.Create(this, () => dragColumn = capturedDisplay));
            builder.AddAttribute(19, "ondragover", EventCallback.Factory.Create(this, () => { }));
            builder.AddEventPreventDefaultAttribute(20, "ondragover", true);
            builder.AddAttribute(21, "ondrop", EventCallback.Factory.Create(this, () => OnColumnDrop(capturedDisplay)));
            builder.AddAttribute(22, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, args => OnHeaderKeyDown(args, capturedDisplay)));

            // The label is a button so sorting stays keyboard-accessible and clicks
            // on the resize handle don't trigger a sort.
            builder.OpenElement(25, "button");
            builder.AddAttribute(26, "type", "button");
            builder.AddAttribute(27, "class", "dx-grid-th-label");
            builder.AddAttribute(28, "title", "Click to sort; Shift+Click for multi-column sort");
            builder.AddAttribute(48, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, e => SortByAsync(actual, e.ShiftKey)));
            builder.AddContent(29, column.Header);
            builder.AddContent(30, SortIndicatorFor(actual));
            builder.CloseElement();

            builder.OpenElement(31, "span");
            builder.AddAttribute(32, "class", "dx-grid-resize");
            builder.AddAttribute(33, "aria-hidden", "true");
            builder.AddAttribute(34, "title", "Drag to resize");
            builder.AddAttribute(35, "onpointerdown",
                EventCallback.Factory.Create<PointerEventArgs>(this, e => StartColumnResize(actual, e.ClientX)));
            builder.AddEventStopPropagationAttribute(36, "onpointerdown", true);
            builder.AddEventPreventDefaultAttribute(37, "ondragstart", true);
            builder.CloseElement();

            if (ShowFilterMenu)
            {
                BuildFilterMenu(builder, actual, column.Header);
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    // A per-column "Excel-style" value filter: a funnel toggle and, when open, a
    // checklist of the column's distinct values (checked = included).
    private void BuildFilterMenu(RenderTreeBuilder builder, int actual, string header)
    {
        bool active = HasValueFilter(actual);
        builder.OpenElement(160, "button");
        builder.AddAttribute(161, "type", "button");
        builder.AddAttribute(162, "class", active ? "dx-grid-funnel dx-grid-funnel-active" : "dx-grid-funnel");
        builder.AddAttribute(163, "aria-label", $"Filter {header}");
        builder.AddAttribute(164, "aria-expanded", openMenuColumn == actual ? "true" : "false");
        builder.AddAttribute(165, "onclick",
            EventCallback.Factory.Create(this, () => openMenuColumn = openMenuColumn == actual ? -1 : actual));
        builder.AddContent(166, "▾");
        builder.CloseElement();

        if (openMenuColumn != actual)
        {
            return;
        }

        builder.OpenElement(167, "div");
        builder.AddAttribute(168, "class", "dx-grid-valuemenu");
        builder.AddAttribute(169, "role", "group");

        foreach (string value in DistinctValues(actual))
        {
            string captured = value;
            builder.OpenElement(170, "label");
            builder.SetKey(value);
            builder.AddAttribute(171, "class", "dx-grid-valuemenu-item");

            builder.OpenElement(172, "input");
            builder.AddAttribute(173, "type", "checkbox");
            builder.AddAttribute(174, "checked", IsValueChecked(actual, value));
            builder.AddAttribute(175, "onchange", EventCallback.Factory.Create(this, () => ToggleValueFilter(actual, captured)));
            builder.CloseElement();

            builder.AddContent(176, value.Length == 0 ? "(blank)" : value);
            builder.CloseElement();
        }

        builder.OpenElement(177, "button");
        builder.AddAttribute(178, "type", "button");
        builder.AddAttribute(179, "class", "dx-grid-valuemenu-clear");
        builder.AddAttribute(180, "disabled", !active);
        builder.AddAttribute(181, "onclick", EventCallback.Factory.Create(this, () => ClearValueFilter(actual)));
        builder.AddContent(182, "Clear filter");
        builder.CloseElement();

        builder.CloseElement();
    }

    private void OnColumnDrop(int displayPosition)
    {
        if (dragColumn >= 0 && dragColumn != displayPosition)
        {
            MoveColumn(dragColumn, displayPosition);
        }

        dragColumn = -1;
    }

    // Ctrl+Arrow reorders the column (Alt+Arrow would clash with browser back/forward).
    private void OnHeaderKeyDown(KeyboardEventArgs args, int displayPosition)
    {
        if (!args.CtrlKey)
        {
            return;
        }

        if (args.Key == "ArrowLeft")
        {
            MoveColumn(displayPosition, displayPosition - 1);
        }
        else if (args.Key == "ArrowRight")
        {
            MoveColumn(displayPosition, displayPosition + 1);
        }
    }

    private void BuildBody(RenderTreeBuilder builder, string columnTemplate)
    {
        builder.OpenElement(18, "div");
        builder.AddAttribute(19, "class", "dx-grid-body");

        // A rowgroup so the data rows have a valid required parent. The keyboard-nav
        // tab stop and aria-activedescendant live on the role=grid container above.
        builder.AddAttribute(192, "role", "rowgroup");

        // Top spacer: occupies the height of the rows scrolled out of view above.
        builder.OpenElement(20, "div");
        builder.AddAttribute(21, "style", $"height:{TopPadding}px;");
        builder.CloseElement();

        foreach (GridRowSlot slot in VisibleSlots())
        {
            if (slot.IsGroupHeader)
            {
                BuildGroupHeader(builder, slot.GroupIndex);
            }
            else
            {
                BuildDataRow(builder, slot.RowIndex, columnTemplate);
                if (HasDetail && IsRowExpanded(slot.RowIndex))
                {
                    BuildDetailRow(builder, slot.RowIndex);
                }
            }
        }

        // Bottom spacer: the height of the rows below the rendered window.
        builder.OpenElement(30, "div");
        builder.AddAttribute(31, "style", $"height:{BottomPadding}px;");
        builder.CloseElement();

        builder.CloseElement();
    }

    private void BuildDataRow(RenderTreeBuilder builder, int rowIndex, string columnTemplate)
    {
        // In remote mode a scrolled-into-view row may not be fetched yet: show a skeleton.
        if (RemoteMode && !IsRowLoaded(rowIndex))
        {
            BuildLoadingRow(builder, columnTemplate);
            return;
        }

        TRow row = RowAt(rowIndex);
        bool selected = IsRowSelected(rowIndex);
        int capturedRow = rowIndex;
        builder.OpenElement(22, "div");
        builder.SetKey(rowIndex);
        builder.AddAttribute(23, "class", selected ? "dx-grid-row dx-grid-row-selected" : "dx-grid-row");
        builder.AddAttribute(24, "role", "row");
        if (ShowSelection)
        {
            builder.AddAttribute(25, "aria-selected", selected ? "true" : "false");
        }

        builder.AddAttribute(26, "style", $"height:{RowHeight}px;{columnTemplate}");

        if (ShowSelection)
        {
            builder.OpenElement(86, "div");
            builder.AddAttribute(87, "class", $"dx-grid-cell dx-grid-select-cell{SelectPinClass()}");
            builder.AddAttribute(88, "role", "gridcell");
            builder.AddAttribute(99, "style", SelectPinStyle());

            builder.OpenElement(89, "input");
            builder.AddAttribute(90, "type", "checkbox");
            builder.AddAttribute(91, "class", "dx-grid-select");
            builder.AddAttribute(92, "aria-label", "Select row");
            builder.AddAttribute(93, "checked", selected);
            builder.AddAttribute(94, "onchange", EventCallback.Factory.Create(this, () => ToggleRow(capturedRow)));
            builder.CloseElement();

            builder.CloseElement();
        }

        bool firstCell = true;
        for (int display = 0; display < Columns.Count; display++)
        {
            int actual = ColumnOrder[display];
            if (IsColumnHidden(actual))
            {
                continue;
            }

            bool editing = IsEditing(rowIndex, actual);
            bool editable = CanEdit(actual);
            bool active = IsActiveCell(rowIndex, display);
            int capturedDisplay = display;

            builder.OpenElement(100, "div");
            builder.AddAttribute(101, "class",
                (editable ? "dx-grid-cell dx-grid-cell-editable" : "dx-grid-cell")
                + PinClass(display)
                + (active ? " dx-grid-cell-active" : string.Empty));
            builder.AddAttribute(102, "role", "gridcell");
            builder.AddAttribute(106, "style", PinStyle(display));
            if (active)
            {
                builder.AddAttribute(119, "id", ActiveCellId);   // aria-activedescendant target
            }

            if (KeyboardNavigation)
            {
                builder.AddAttribute(197, "onclick", EventCallback.Factory.Create(this, () => SetActiveCell(capturedRow, capturedDisplay)));
            }

            if (editable && !editing)
            {
                builder.AddAttribute(103, "ondblclick", EventCallback.Factory.Create(this, () => BeginEdit(capturedRow, actual)));
                builder.AddAttribute(104, "title", "Double-click to edit");
            }

            // The expand twisty lives in the first visible cell when a detail is set.
            if (HasDetail && firstCell)
            {
                bool expanded = IsRowExpanded(rowIndex);
                builder.OpenElement(107, "button");
                builder.AddAttribute(108, "type", "button");
                builder.AddAttribute(109, "class", "dx-grid-detail-toggle");
                builder.AddAttribute(110, "aria-label", expanded ? "Collapse details" : "Expand details");
                builder.AddAttribute(111, "aria-expanded", expanded ? "true" : "false");
                builder.AddAttribute(112, "onclick", EventCallback.Factory.Create(this, () => ToggleRowExpanded(capturedRow)));
                builder.AddContent(113, expanded ? "▾" : "▸");
                builder.CloseElement();
            }

            if (editing)
            {
                BuildCellEditor(builder, row, actual);
            }
            else
            {
                builder.AddContent(105, CellText(row, actual));
            }

            builder.CloseElement();
            firstCell = false;
        }

        builder.CloseElement();
    }

    // A skeleton row shown while a remote window is being fetched.
    private void BuildLoadingRow(RenderTreeBuilder builder, string columnTemplate)
    {
        builder.OpenElement(200, "div");
        builder.AddAttribute(201, "class", "dx-grid-row dx-grid-row-loading");
        builder.AddAttribute(202, "role", "row");
        builder.AddAttribute(203, "aria-busy", "true");
        builder.AddAttribute(204, "style", $"height:{RowHeight}px;{columnTemplate}");

        for (int display = 0; display < Columns.Count; display++)
        {
            int actual = ColumnOrder[display];
            if (IsColumnHidden(actual))
            {
                continue;
            }

            builder.OpenElement(205, "div");
            builder.AddAttribute(206, "class", $"dx-grid-cell{PinClass(display)}");
            builder.AddAttribute(207, "style", PinStyle(display));
            builder.OpenElement(208, "span");
            builder.AddAttribute(209, "class", "dx-grid-skeleton");
            builder.CloseElement();
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    // A full-width panel rendered beneath an expanded row, hosting the detail template.
    private void BuildDetailRow(RenderTreeBuilder builder, int rowIndex)
    {
        builder.OpenElement(114, "div");
        builder.SetKey($"detail-{rowIndex}");
        builder.AddAttribute(115, "class", "dx-grid-detail");
        builder.AddAttribute(116, "role", "row");
        builder.AddContent(117, DetailTemplate!(RowAt(rowIndex)));
        builder.CloseElement();
    }

    private void BuildCellEditor(RenderTreeBuilder builder, TRow row, int actual)
    {
        builder.OpenElement(110, "input");
        builder.AddAttribute(111, "class", "dx-grid-edit-input");
        builder.AddAttribute(112, "type", "text");
        builder.AddAttribute(113, "value", EditDraft);
        builder.AddAttribute(114, "aria-label", $"Edit {Columns[actual].Header}");
        builder.AddAttribute(115, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, e => SetEditDraft(e.Value as string)));
        builder.AddAttribute(116, "onchange", EventCallback.Factory.Create(this, CommitEditAsync));
        builder.AddAttribute(117, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnEditKeyDownAsync));
        builder.AddEventStopPropagationAttribute(213, "onkeydown", true);   // don't bubble to cell nav
        builder.AddElementReferenceCapture(118, element => EditInput = element);
        builder.CloseElement();
    }

    private Task OnEditKeyDownAsync(KeyboardEventArgs args) => args.Key switch
    {
        "Enter" => CommitEditAsync(),
        "Escape" => Run(CancelEdit),
        _ => Task.CompletedTask,
    };

    private static Task Run(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private void BuildGroupHeader(RenderTreeBuilder builder, int groupIndex)
    {
        bool expanded = GroupExpanded(groupIndex);
        int captured = groupIndex;

        builder.OpenElement(40, "div");
        builder.SetKey($"g{GroupKey(groupIndex)}");
        builder.AddAttribute(41, "class", "dx-grid-group");
        builder.AddAttribute(42, "role", "row");
        builder.AddAttribute(43, "style", $"height:{RowHeight}px;");
        builder.AddAttribute(44, "aria-expanded", expanded ? "true" : "false");
        builder.AddAttribute(45, "onclick", EventCallback.Factory.Create(this, () => ToggleGroup(captured)));

        builder.OpenElement(46, "span");
        builder.AddAttribute(47, "class", "dx-grid-group-toggle");
        builder.AddContent(48, expanded ? "▾" : "▸");
        builder.CloseElement();

        builder.OpenElement(49, "span");
        builder.AddAttribute(50, "class", "dx-grid-group-label");
        builder.AddContent(51, $"{GroupColumnHeader}: {GroupKey(groupIndex)}");
        builder.CloseElement();

        builder.OpenElement(52, "span");
        builder.AddAttribute(53, "class", "dx-grid-group-count");
        builder.AddContent(54, $"({GroupRowCount(groupIndex)})");
        builder.CloseElement();

        if (ShowGroupAggregates)
        {
            BuildGroupSubtotals(builder, groupIndex);
        }

        builder.CloseElement();
    }

    private void BuildGroupSubtotals(RenderTreeBuilder builder, int groupIndex)
    {
        builder.OpenElement(55, "span");
        builder.AddAttribute(56, "class", "dx-grid-group-summary");

        for (int columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
        {
            if (AggregationKind(columnIndex) is not GridAggregateKind kind)
            {
                continue;
            }

            builder.OpenElement(57, "span");
            builder.AddContent(58, $"{Columns[columnIndex].Header} {FormatAggregate(kind, GroupAggregate(groupIndex, columnIndex))}");
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private string AriaSortFor(int columnIndex)
    {
        (bool found, bool descending, _) = SortStateOf(columnIndex);
        return found ? (descending ? "descending" : "ascending") : "none";
    }

    private string SortIndicatorFor(int columnIndex)
    {
        (bool found, bool descending, int order) = SortStateOf(columnIndex);
        if (!found)
        {
            return string.Empty;
        }

        string arrow = descending ? " ▼" : " ▲";
        // Show the priority number only when sorting by more than one column.
        return SortKeyCount > 1 ? $"{arrow}{order}" : arrow;
    }
}
