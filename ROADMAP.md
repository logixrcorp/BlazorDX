# BlazorDX Roadmap

The foundation goal — *prove a secure-by-default, AOT-safe, headless component
system is possible* — is met, and the catalog has grown well past the original plan:
**~65 components** on a shared headless engine, a deep DataGrid family (flat / tree /
pivot), data visualization, scheduling, editors, file management, AI chat, and
standards-verified barcodes/QR — all trim-clean. See [COMPONENTS.md](COMPONENTS.md)
and [ARCHITECTURE.md](ARCHITECTURE.md).

This roadmap is now about **finishing the library to a coherent 1.0** and **earning
adoption**, deliberately scoped so we deepen our identity instead of chasing a
commercial suite feature-for-feature.

## What "complete" means here

Complete = **line-of-business-complete + hardened + trusted**, *not* "matches
Syncfusion." Concretely:

- the components a typical app reaches for (Bucket 1 below) exist,
- the flagship DataGrid can bind to a server,
- the differentiating claims are independently verified,
- and there is enough documentation, accessibility evidence, and review for someone
  to bet on it.

Raw component count is explicitly **not** the target — see *Out of scope*.

## Done

- **Engine**: anchored positioning (flip/shift), dismiss layer, focus trap,
  roving-tabindex, selection state, generalized `DxVirtualize<T>`, drag-reorder,
  theme tokens, `PresenceBoundary` motion.
- **DataGrid (in-memory)**: sort, multi-column sort, per-column filter, Excel-style
  value filter, column chooser, group + per-group aggregates (Rust), row selection,
  inline edit (zero-reflection write-back), column reorder, column resize, pinned
  columns, master/detail rows, CSV export, clipboard copy, keyboard cell navigation,
  tree data, pivot.
- **Catalog**: overlays, inputs, navigation/layout, ~11 chart/gauge/sparkline types,
  scheduling (Scheduler + Gantt), editors (Markdown + WYSIWYG + QueryBuilder), file
  manager, AI chat, hotkeys, and pure-C# **EAN-13 / Code 128 / QR** verified against
  published reference vectors. Six `[JSImport]` TS bridges; five Rust kernels with
  managed parity.
- **Globalization & packaging**: culture-aware formatting + RTL; eight NuGet packages
  (incl. analyzer/source-gen) packed clean.
- **Proof**: ~310 tests green (bUnit + compute + analyzer) + Rust `cargo test`;
  **trim-clean publish** under `IsTrimmable`/`IsAotCompatible` + warnings-as-errors;
  1000-line cap holding via DX1000.
- **Showcase**: the demo home page is now a developer landing — live previews,
  getting-started code, and a 50-card catalog with copy-pasteable examples per
  component, behind a categorized nav.

---

## Track 1 — Breadth (Bucket 1): get to ~100, LOB-complete

Composition over the existing engine; days each, highly parallelizable. This is the
*only* breadth work that matters for the library's identity.

- **Primitives/leaves**: ButtonGroup, SplitButton, DropdownButton, Toolbar, Chip/Tag,
  Badge, Avatar, Card, FAB/SpeedDial, Affix/BackTop, Result/Empty/Statistic.
- **Inputs**: Slider / RangeSlider (the `DxRating` ARIA-slider pattern generalizes),
  ColorPicker, MaskedTextBox, TimePicker / DateRange / DateTimePicker (extend
  `DxDatePicker`), Mention, standalone AutoComplete, TreeView (TreeGrid minus columns).
- **Layout**: Splitter / SplitPane, resizable/dock panels, month-view Calendar,
  Kanban board (Sortable + Tiles already supply drag).
- **Data viz**: radar, bubble, candlestick, funnel, heatmap, treemap, sankey
  (pure SVG, like the existing ten).
- **Upload**: drag-drop FileUpload with progress (one small `[JSImport]` bridge).

## Track 2 — Depth (the high-value mediums)

- **DataGrid `IDataSource`** — push paging/sort/filter to a backend. *The single most
  important enterprise unlock*; changes the grid from "100k rows in memory" to "10M on
  a server." Rank this above any new component.
- **Chart interactivity** — tooltips, legend toggling, zoom/pan over the SVG charts.
- **Grid export depth** — SpreadsheetML (`.xlsx`) and PDF, building on CSV export.

## Track 3 — Trust (the actual binding constraint)

Higher leverage than breadth for adoption. Pairs directly with the senior review.

- **Senior code review** — proof the differentiating claims; see [REVIEW.md](REVIEW.md).
- **Hosted docs site + API reference** — generated where possible; the current
  COMPONENTS/ARCHITECTURE/ADRs + the showcase are the seed.
- **Formal accessibility audit** — screen-reader + axe pass on the primitives; lift
  the self-graded **B** to an attested grade.
- **A reference application** — one real app we actually run and deploy, even small,
  to start a production track record.
- **Native WASM AOT pass** — exercise full `RunAOTCompilation` (not just IL trimming)
  end-to-end and document the capability matrix per render mode.

---

## Out of scope by design

These are separate products (server-side document engines, not UI components), and
pursuing them would dilute the headless/auditable identity that *is* the
differentiator. Where it makes sense, we expose clean integration seams instead:

- Document processing (PDF/Word/Excel generation & parsing)
- Report designer
- Full in-browser spreadsheet
- PDF viewer
- Mapping / GIS
- Outlook-depth recurring scheduler, diagram/flowchart engine

## Sequencing

1. **Track 3 (review + a11y audit + docs scaffold)** in parallel with
2. **Track 1 (Bucket-1 breadth)** — fast, on-brand, gets to LOB-complete, and is the
   natural place to keep building.
3. **Track 2 (`IDataSource`, then chart interactivity, then export depth)** — the
   competitive unlocks, taken one at a time.

Breadth and trust are run together, not serialized — that is the thing worth
optimizing for. Component count alone moves the breadth grade; only the trust track
moves *adoption*.
