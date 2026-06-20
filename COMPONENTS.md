# BlazorDX Component Catalog

Every styled `Dx*` component below is a thin **Tier 2** wrapper over a headless
**Tier 1** primitive (behavior + WAI-ARIA, no CSS). You can restyle any component
through CSS variables — see [ARCHITECTURE.md](ARCHITECTURE.md) — or drop to the
primitive and supply your own markup. Each row links to its live demo route in the
sample app (`samples/BlazorDX.Demo`).

## Overlays & popups

| Component | What it does | Demo |
|---|---|---|
| `DxDialog` | Modal dialog: focus trap, scroll lock, Esc/click-outside dismiss, exit animation | `/dialog` |
| `DxSheet` | Edge-anchored offcanvas panel (a `DialogPrimitive` that slides in) | `/overlays` |
| `DxPopover` | Anchored popover with collision flip/shift | `/popover` |
| `DxTooltip` | Hover/focus tooltip, anchored + flipping | `/menu` |
| `DxMenu` | Click menu: roving-tabindex keyboard nav, ARIA menu/menuitem | `/menu` |
| `DxContextMenu` | Right-click menu positioned at the cursor | `/overlays` |
| `DxCommandPalette` | ⌘K palette: dialog + typeahead filter + run-on-Enter | `/command` |

## Selection & input

| Component | What it does | Demo |
|---|---|---|
| `DxSelect<T>` | Single-select dropdown (positioning + dismiss + roving + selection) | `/select` |
| `DxListbox<T>` | Inline single/multi list, `@bind-Values` | `/select` |
| `DxComboBox<T>` | Typeahead filter + dropdown (aria-activedescendant) | `/select` |
| `DxTransferList` | Dual-listbox move-between (composition of two `DxListbox`) | `/transfer` |
| `DxCheckbox` / `DxSwitch` | Native-backed boolean inputs | `/controls` |
| `DxRadioGroup<T>` | Roving-tabindex radio group | `/controls` |
| `DxTextBox` / `DxTextArea` / `DxPassword` | Text inputs (password has a reveal toggle) | `/controls` |
| `DxNumeric<T>` | Generic-math numeric input + stepper (`INumber<T>`, no reflection) | `/controls` |
| `DxRating` | Star rating, ARIA slider | `/rating` |
| `DxDatePicker` | Calendar popover with 2-D keyboard navigation | `/pickers` |

## Navigation & layout

| Component | What it does | Demo |
|---|---|---|
| `DxTabs` / `DxAccordion` | Content organization (roving / expand state) | `/layout` |
| `DxBreadcrumbs` / `DxDivider` | Trail + separators | `/structure` |
| `DxDrawer` | Persistent push-layout side panel (non-modal counterpart to `DxSheet`) | `/structure` |
| `DxTimeline` / `DxCarousel` | Event timeline + keyboard-navigable slide rotator | `/structure` |
| `DxPager` | Pagination with windowed page numbers | `/paging` |
| `DxStepper` | Wizard / step flow | `/wizard` |
| `DxTileLayout` | Reorderable dashboard tiles (drag + Alt+Arrow) | `/tiles` |
| `DxSortableList` | Drag/keyboard reorderable list | `/sortable` |
| `DxVirtualize<T>` | Generalized windowing/virtualization | `/virtualize` |
| `DxThemeProvider` | Token theming (light/dark + accent) for a subtree | `/theme` |

## Data grids

| Component | What it does | Demo |
|---|---|---|
| `DxDataGrid<TRow>` | Virtualized grid: sort, per-column filter, group, aggregate (Rust), row selection, inline edit, column reorder/resize, pinned columns — all zero-reflection | `/grid` |
| `DxTreeGrid<TRow>` | Hierarchical tree-table (flatten + virtualize, shared accessor) | `/tree` |
| `DxPivotGrid<TRow>` | Cross-tab reusing the Rust aggregation kernels | `/pivot` |

## Data visualization

| Component | What it does | Demo |
|---|---|---|
| `DxLineChart` / `DxAreaChart` | Series charts, LTTB-downsampled by Rust/wasm | `/charts` |
| `DxBarChart` / `DxPieChart` | Categorical bar (V/H) + pie/donut with legend | `/charts` |
| `DxHistogram` | Bins values via the Rust histogram kernel | `/charts` |
| `DxSparkline` | Inline line/bar trend | `/charts` |
| `DxRadialGauge` / `DxLinearGauge` | Meter gauges (270° arc / zoned bar) | `/charts` |

## Scheduling

| Component | What it does | Demo |
|---|---|---|
| `DxScheduler` | Week calendar with time-positioned event blocks | `/scheduler` |
| `DxGantt` | Task timeline bars with progress fills | `/gantt` |

## Editors, files & AI

| Component | What it does | Demo |
|---|---|---|
| `DxMarkdown` / `DxMarkdownEditor` | Safe Markdown renderer (encode-then-format) + live editor | `/markdown` |
| `DxRichTextEditor` | WYSIWYG over contentEditable, routed through an **injected** sanitizer | `/richtext` |
| `DxChat` | AI chat surface (assistant turns render via `DxMarkdown`) | `/chat` |
| `DxFileManager` | Two-pane folder tree + contents with breadcrumb | `/files` |

## Feedback

| Component | What it does | Demo |
|---|---|---|
| `DxToastHost` | Scoped toast notifications | `/feedback` |
| `DxAlert` / `DxSpinner` / `DxProgress` / `DxSkeleton` | Status, loading, progress, placeholder | `/feedback` |

---

## Under the hood

- **Rust → wasm kernels** (`dx_grid`): `sort_indices`, `filter_indices_gte`,
  `aggregate` (count/sum/min/max/mean), `histogram` (binning), `downsample_lttb` —
  each with a managed C# fallback, so every component works in SSR and on the server.
- **Source generators**: `[GridRow]`/`[GridColumn]` emit a reflection-free
  `IGridRowAccessor<TRow>` (read **and** write-back) — no `PropertyInfo`.
- **`[JSImport]` bridges** (TypeScript → minified ESM): grid-dom, overlay,
  positioning, grid-interop, richtext. No `IJSRuntime`.
- **Governance**: hard 1000-line file cap (DX1000 analyzer + build target),
  `MarkupString` ban (DX1001) routed through the sanitizer, singleton-UI-state ban
  (DX1002), warnings-as-errors, `IsTrimmable`/`IsAotCompatible` — the whole catalog
  publishes trim-clean.

See [README.md](README.md) for prerequisites and [ARCHITECTURE.md](ARCHITECTURE.md)
plus [docs/adr](docs/adr) for the decisions behind all of this.
