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
| `DxHotkeys` | Registers global keyboard shortcuts, matched in JS so the browser default can be suppressed synchronously; renders nothing | `/hotkeys` |
| `DxKeyboardShortcuts` | Cheat-sheet overlay listing your `DxHotkeys` shortcuts (press `?`) | `/hotkeys` |

## Selection & input

| Component | What it does | Demo |
|---|---|---|
| `DxForm<TModel>` | Source-generated form: renders + validates a `[DxFormModel]`, and the same descriptor projects to an AI tool (`FormTool`) | `/forms` |
| `DxFormSection` | Labelled, collapsible field group within a form | `/forms` |
| `DxFormGrid` | Responsive multi-column field layout within a form | `/forms` |
| `DxFormField` | Renders one form field by name, for manual layout | `/forms` |
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
| `DxTimePicker` | Time input backed by the native time picker | `/pickers` |
| `DxDateRangePicker` | Start/end date range, composing two `DxDatePicker`s | `/pickers` |
| `DxColorPicker` | Native color input with hex readout and preset swatches | `/controls` |
| `DxMaskedTextBox` | Text input that formats against a mask | `/controls` |
| `DxRangeSlider` | Dual-thumb range slider, clamped so thumbs never cross | `/controls` |
| `DxFileUpload` | Drag-drop upload zone over the native `InputFile` | `/controls` |
| `DxVirtualKeyboard` | On-screen QWERTY for touch/kiosk/a11y text entry | `/keyboard` |

## Buttons & display

| Component | What it does | Demo |
|---|---|---|
| `DxButton` / `DxButtonGroup` | Styled button (attribute splatting) + a segmented group of adjacent buttons | `/elements` |
| `DxToolbar` | Horizontal toolbar container (`role="toolbar"`) | `/elements` |
| `DxSplitButton` | Primary action + a caret menu (composes `DxMenu`) | `/elements` |
| `DxBadge` | Status pill or count badge; six variants + a dot | `/elements` |
| `DxKbd` | Renders a shortcut as styled `<kbd>` key caps | `/hotkeys` |
| `DxChip` | Compact tag, optionally dismissible | `/elements` |
| `DxAvatar` | Circular avatar — image or initials fallback | `/elements` |
| `DxCard` | Surface container with an optional header/footer | `/elements` |
| `DxSlider` | Numeric slider backed by a native range input | `/elements` |

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
| `DxKanban` | Board of draggable cards across columns | `/kanban` |
| `DxSortableList` | Drag/keyboard reorderable list | `/sortable` |
| `DxVirtualize<T>` | Generalized windowing/virtualization | `/virtualize` |
| `DxTreeView` | Hierarchical tree with expand/collapse + keyboard nav | `/structure` |
| `DxSplitter` | Two resizable panes with a draggable divider | `/structure` |
| `DxThemeProvider` | Token theming (light/dark + accent) for a subtree | `/theme` |
| `DxSkipLink` | WCAG 2.4.1 bypass-blocks link, offscreen until focused — used on every page in this site's own layout | `/` |

## Data grids

| Component | What it does | Demo |
|---|---|---|
| `DxDataGrid<TRow>` | Virtualized grid: sort, per-column filter, group, aggregate (Rust), row selection, inline edit, column reorder/resize, pinned columns — all zero-reflection | `/grid` |
| `DxTreeGrid<TRow>` | Hierarchical tree-table (flatten + virtualize, shared accessor) | `/tree` |
| `DxPivotGrid<TRow>` | Cross-tab reusing the Rust aggregation kernels | `/pivot` |

## Data visualization

| Component | What it does | Demo |
|---|---|---|
| `DxGraph` | A single dynamic entry point: a runtime `Kind` (`GraphKind`) switch over the 18 chart kinds below whose data reduces to `ChartPoint`, a `ChartTreeNode` root, or a bare scalar/sample list — see its own doc comment for why the other 7 (Bullet/BoxPlot/Sankey/NetworkGraph/ParallelCoordinates/WordCloud/ChordDiagram) stay separate | `/charts` |
| `DxLineChart` / `DxAreaChart` | Series charts, LTTB-downsampled by Rust/wasm | `/charts` |
| `DxBarChart` / `DxPieChart` | Categorical bar (V/H) + pie/donut with legend | `/charts` |
| `DxHistogram` | Bins values via the Rust histogram kernel | `/charts` |
| `DxSparkline` | Inline line/bar trend | `/charts` |
| `DxRadialGauge` / `DxLinearGauge` | Meter gauges (270° arc / zoned bar) | `/charts` |
| `DxScatterChart` / `DxStackedBarChart` | XY scatter plot + stacked/grouped series bars | `/charts` |
| `DxRadarChart` / `DxFunnelChart` | Spider chart over shared axes + conversion funnel | `/charts` |
| `DxCandlestickChart` | OHLC candlesticks coloured by direction | `/charts` |
| `DxWaterfallChart` | Running-total bars; a point with `Y2` set is an absolute total that resets the running total | `/charts` |
| `DxBubbleChart` | Scatter plot with a third dimension (`Y2`) encoded as radius | `/charts` |
| `DxHeatmap` | Row (`Series`) x column (`Category`) grid, intensity via fill-opacity | `/charts` |
| `DxBulletChart` | KPI vs. target vs. qualitative range bands (Stephen Few's bullet-graph design) | `/charts` |
| `DxTreemap` / `DxSunburst` | Squarified nested rectangles / radial treemap, sized by `ChartTreeNode.Value` | `/charts` |
| `DxBoxPlot` | Q1/median/Q3 box, whiskers, outliers beyond 1.5x IQR, optional violin density silhouette | `/charts` |
| `DxSankeyChart` | Layered flow diagram; ribbon thickness scaled to `SankeyLink.Value` | `/charts` |
| `DxNetworkGraph` | Force-directed graph; connected nodes cluster, unconnected ones drift apart | `/charts` |
| `DxParallelCoordinates` | One axis per dimension; each row is a polyline crossing every axis at its own value | `/charts` |
| `DxWordCloud` | Spiral-packed words sized by weight (largest first) | `/charts` |
| `DxChordDiagram` | Nodes as arcs sized by total flow; ribbons connect a proportional slice of each endpoint | `/charts` |

## Scheduling

| Component | What it does | Demo |
|---|---|---|
| `DxCalendar` | Inline month calendar: single or range date selection, marker dots, disabled dates, a per-day template, culture-aware week start | `/calendar` |
| `DxScheduler` | Week / Month / Day calendar with time-positioned event blocks, RRULE-style recurrence, and pointer drag-to-move / drag-to-create | `/scheduler` |
| `DxGantt` | Task timeline bars with progress fills | `/gantt` |

## Editors, files & AI

| Component | What it does | Demo |
|---|---|---|
| `DxMarkdown` / `DxMarkdownEditor` | Safe Markdown renderer (encode-then-format) + live editor | `/markdown` |
| `DxRichTextEditor` | WYSIWYG over contentEditable, routed through an **injected** sanitizer | `/richtext` |
| `DxChat` | AI chat surface (assistant turns render via `DxMarkdown`) | `/chat` |
| `DxFileManager` | Two-pane folder tree + contents with breadcrumb, hybrid drag-and-drop, and opt-in two-sided SHA-256 upload integrity verification | `/files` |
| `DxQueryBuilder` | Visual, nestable predicate tree (AND/OR groups) | `/query` |
| `DxImageEditor` | Canvas image editor: adjust, filter, rotate/flip, export — via a TS bridge | `/imageeditor` |

## Documents & reporting

Heavy parsers and external SDKs are quarantined in opt-in packages (`BlazorDX.Documents`,
`BlazorDX.Integrations.Reporting`, `BlazorDX.Integrations.PowerBI`, `BlazorDX.Htmx`); report
credentials stay server-side. See [roadmap-documents-and-reporting.md](roadmap-documents-and-reporting.md)
and [ADR-0010](adr/0010-documents-and-reporting-integration.md).

| Component | What it does | Demo |
|---|---|---|
| `DxDocumentViewer` | Native-embed viewer for PDF + browser-renderable documents (toolbar + iframe shell, no parser) | `/docviewer` |
| `DxSpreadsheetViewer` | Excel (.xlsx) viewer/editor with a live formula recalc graph; hand-rolled OOXML, no external deps | `/excel`, `/excel-edit` |
| `DxWordViewer` / `DxWordEditor` | Word (.docx) viewer + editor over a sanitized OOXML↔HTML round-trip | `/word`, `/word-edit` |
| `DxReportViewer` | Server-side SSRS rendering via Microsoft's URL-access engine, delivered over HTMX | `/reports` |
| `DxPowerBiReport` | Lazy-loaded wrapper over `powerbi-client`; embed token minted server-side | `/powerbi` |
| `DxHtmxDocumentViewer` | Static-SSR, read-only PDF/Excel/Word viewer with a no-JS fallback | `/htmx/doc` |

## Feedback

| Component | What it does | Demo |
|---|---|---|
| `DxToastHost` | Scoped toast notifications | `/feedback` |
| `DxAlert` / `DxSpinner` / `DxProgress` / `DxSkeleton` | Status, loading, progress, placeholder | `/feedback` |
| `DxErrorBoundary` | Contains a thrown exception; shows a retryable fallback and reports to `IDxDiagnostics` | `/errors` |

## Barcodes & QR

| Component | What it does | Demo |
|---|---|---|
| `DxQrCode` | QR code (versions 1–4, all EC levels), verified against published vectors | `/barcodes` |
| `DxBarcode` | Code 128 (Set B) with an independent decoder round-trip | `/barcodes` |
| `DxEan13` | EAN-13 retail barcode with a computed check digit | `/barcodes` |

## Editorial & long-form

Magazine-style layout components for articles, blog posts, and whitepapers — hero, pull-quotes,
technical sidebars, a scroll-revealed narrative section, a two-column "spread," a three-card
footer. Built entirely on `dx-theme.css` tokens (no new color system). See it composed end to
end in the flagship piece at `/insights/articles/zero-trust-ephemeral-chat-conduit`.
Two stylesheets: `dx-editorial.css` (core layout) and `dx-editorial-extras.css`
(reading-experience/discovery add-ons below, split out once the core file hit the library's own
1000-line cap) — load both.

| Component | What it does | Demo |
|---|---|---|
| `DxEditorialLayout` | The shell: hero (kicker/title/subtitle/byline, optionally a full-bleed photo) + a content slot, wraps a `DxEditorialFooter` on automatically | `/insights/articles/zero-trust-ephemeral-chat-conduit` |
| `DxEditorialFigure` | A full-bleed narrative-break image | `/insights/articles/zero-trust-ephemeral-chat-conduit` |
| `DxEditorialSpread` | A two-column "classic meets modern" spread: an elevated, drop-shadowed photo against body copy, with a labeled spec card overlapping its corner | `/insights/articles/zero-trust-ephemeral-chat-conduit` |
| `DxEditorialPullQuote` | A large, italicized, serif-accented pull-quote | `/insights/articles/zero-trust-ephemeral-chat-conduit` |
| `DxEditorialSidebar` | A floating technical-spec card that doesn't interrupt the reading flow | `/insights/articles/zero-trust-ephemeral-chat-conduit` |
| `DxEditorialScrollytelling` / `DxEditorialScrollyStage` | A scroll-revealed narrative sequence — `IntersectionObserver`-only, never a scroll-position listener. Requires a companion `<script type="module" src="_content/BlazorDX.Components/dx-editorial-scrollytelling.js">` tag, added once alongside the `dx-editorial.css` `<link>` | `/insights/articles/zero-trust-ephemeral-chat-conduit` |
| `DxEditorialDissipation` | A CSS-only "data-as-art" dot-grid dissolve, no canvas/WebGL | `/insights/articles/zero-trust-ephemeral-chat-conduit` |
| `DxEditorialFooter` | A three-card footer grid, with sensible defaults | `/insights/articles/zero-trust-ephemeral-chat-conduit` |
| `DxEditorialTableOfContents` | A jump-link contents list — the web descendant of a print magazine's contents page | `/insights/articles/zero-trust-ephemeral-chat-conduit` |
| `DxEditorialReadingProgress` | A fixed top progress bar, filled via scroll-driven CSS (`animation-timeline: scroll(root)`) — no scroll-position listener | `/insights/articles/zero-trust-ephemeral-chat-conduit` |
| `DxEditorialDropCap` | An enlarged first-letter treatment (`::first-letter`) for an opening paragraph — the oldest device in the magazine glossary | `/insights/articles/zero-trust-ephemeral-chat-conduit` |
| `DxEditorialAuthorBio` | A richer "about the author" block composing `DxAvatar` — avatar, name, role, and a short bio | `/insights/articles/zero-trust-ephemeral-chat-conduit` |
| `DxEditorialTagList` | A row of topic pills, each a real link (not `DxChip`, which has no href) | not wired into a live piece yet — no topic archive exists to link to |
| `DxEditorialRelated` | A "more like this" card row for the end of a piece; renders nothing when `Entries` is empty | not wired into a live piece yet — there's only one published piece to relate |
| `DxEditorialSeriesNav` | Previous/next navigation for a multi-part piece — the web analogue of a print jump line; either side may be omitted | not wired into a live piece yet — no multi-part series exists |
| `DxEditorialInsetFigure` | A small floated image with text wrapping around it via `shape-outside` — a third image treatment alongside `DxEditorialFigure` and `DxEditorialSpread` | not wired into a live piece yet — no spare image asset to demo it with honestly |
| `DxEditorialStatRow` | A row of oversized numeric callouts, the data-journalism "big number" device | `/insights/articles/zero-trust-ephemeral-chat-conduit` |
| `DxEditorialFootnoteRef` / `DxEditorialFootnotes` | An inline superscript marker and its matching footnote list, with a back-link — the web analogue of a print footnote | `/insights/articles/zero-trust-ephemeral-chat-conduit` |
| `DxEditorialGlossaryTerm` | An inline term with a hover/focus definition, composing `DxTooltip` | `/insights/articles/zero-trust-ephemeral-chat-conduit` |

---

## Under the hood

- **Rust → wasm kernels** (`dx_grid`): `sort_indices`, `filter_indices_gte`,
  `aggregate` (count/sum/min/max/mean), `histogram` (binning), `downsample_lttb` —
  each with a managed C# fallback, so every component works in SSR and on the server.
- **Source generators**: `[GridRow]`/`[GridColumn]` emit a reflection-free
  `IGridRowAccessor<TRow>` (read **and** write-back) — no `PropertyInfo`. `[DxFormModel]`
  emits an `IFormModel<T>` that `DxForm` renders **and** projects into an AI tool (JSON-Schema
  over MCP) — secured, audited, with sensitive fields kept from the AI. See
  [docs/ai-integration.md](ai-integration.md).
- **`[JSImport]` bridges** (TypeScript → minified ESM): grid-dom, overlay,
  positioning, grid-interop, richtext. No `IJSRuntime`.
- **Governance**: hard 1000-line file cap (DX1000 analyzer + build target),
  `MarkupString` ban (DX1001) routed through the sanitizer, singleton-UI-state ban
  (DX1002), warnings-as-errors, `IsTrimmable`/`IsAotCompatible` — the whole catalog
  publishes trim-clean.

See [README.md](../README.md) for prerequisites and [ARCHITECTURE.md](ARCHITECTURE.md)
plus [docs/adr](adr) for the decisions behind all of this.
