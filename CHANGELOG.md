# Changelog

All notable changes to BlazorDX are documented here. The format is loosely based on
[Keep a Changelog](https://keepachangelog.com/), and the project aims to follow
[Semantic Versioning](https://semver.org/) once it reaches 1.0.

> **Beta.** BlazorDX is pre-1.0 and built with substantial AI assistance. Breaking
> changes can land in any minor release until 1.0.

## [Unreleased]

### Added

- **`EditorialSpread` — a two-column "classic meets modern" magazine layout** for Insights
  pieces: an elevated, drop-shadowed photo collaged against body copy, with a small labeled
  spec card overlapping its corner (the fashion-editorial "swatch card" device, adapted to show
  a real fact — a test name, a cipher suite — instead of a color chip), and a serif-italic
  kicker over a bold sans title (explicitly mixing "classic" serif with the "modern" sans used
  throughout). Modeled on real print-magazine spread conventions, translated onto BlazorDX's own
  `dx-theme.css` tokens rather than importing new brand colors. Used once in the flagship article
  in place of a full-bleed figure, for rhythm — real magazines vary photo treatment across a
  feature rather than repeating one pattern throughout.

- **An Articles/Blog/Whitepapers "Insights" content area** (`/insights`), with the demo's own
  editorial design system — hero, pull-quotes, technical sidebars, a scroll-revealed narrative
  section, a three-card footer — built entirely on the existing `dx-theme.css` tokens (no
  Tailwind, no new build tooling). `InsightsCatalog` is the single source of truth for what's
  published; Articles/Whitepapers are hand-built Razor pages using the shared `EditorialLayout`,
  Blog posts are Markdown files rendered through `DxMarkdown` via a dynamic
  `/insights/blog/{slug}` route. The scroll reveal is `IntersectionObserver`-only (never a
  scroll-position listener, so nothing runs per scroll pixel) via a component-co-located
  `EditorialScrollytelling.razor.js`, with a `MutationObserver` fallback for Blazor Web App's
  prerender-then-hydrate timing (the initial module-load pass can end up watching DOM nodes the
  WASM runtime later replaces).
  Ships with one real piece — **"The Architecture of Silence"** (`/insights/articles/zero-trust-ephemeral-chat-conduit`),
  a deep-dive on the Ephemeral Chat Conduit's actual architecture (blind-router relay, the
  browser-sandboxed `dx_security` wasm crypto core, closed-shadow-DOM isolation) — written to
  match what the feature actually guarantees, including its real limits (no authorization by
  default, best-effort SSE delivery, best-effort rather than provable erasure). Blog and
  Whitepapers ship as working, empty sections rather than seeded placeholder content.
  The flagship article uses a hero image plus four `EditorialFigure` narrative-break images
  (provider-to-browser routing, the closed-shadow-root boundary, session-end erasure, and a
  closing image), all resized/recompressed to 2000px-wide JPEGs (~150KB each, down from
  1.2–12MB source files) to keep the page's load weight in line with the rest of the site.
  `EditorialLayout` gained an optional `HeroImageSrc`/`HeroImageAlt` pair (eager-loaded, since
  it's the page's LCP element); the four in-body figures use `loading="lazy"`.
  The flagship article's presentation got a Runway/Vogue-style pass: the hero and all four
  figures break out to full viewport width regardless of the page shell's 1000px `<main>` cap,
  via a pure `margin-left/right: calc(50% - 50vw)` breakout (guarded by `overflow-x: hidden` on
  `.dx-editorial` against the vw-includes-scrollbar sub-pixel overflow it introduces); the hero
  runs full-viewport-height with the title/subtitle overlaid on a full-image ambient tint plus a
  stronger bottom gradient (needed for reliable contrast against an arbitrary photo, not just its
  darkest corner) and a one-shot Ken Burns zoom; figures crop to a cinematic 2:1 (4:5 on narrow
  viewports) and get a scroll-linked reveal via CSS `animation-timeline: view()` —
  GPU-composited and declarative, so it costs nothing per scroll frame and isn't a
  scroll-position listener — behind `@supports`, degrading to a static image elsewhere. The
  pull-quote gained an oversized ghost quotation mark and the scrollytelling stages a giant ghost
  numeral watermark (via a `data-index` attribute + `content: attr()`), both classic
  magazine-spread devices. All motion respects `prefers-reduced-motion`.

  An earlier version of this breakout used `position: relative; left: 50%; margin-left: -50vw`,
  which visibly clipped the leading character of the hero's overlaid text in a real browser
  (first misread during review as a screenshot-tool-only artifact — it wasn't; direct DOM
  inspection showed complete/correctly-positioned text because the bug was in paint, not layout,
  so geometry queries didn't catch it). Switching to the plain `margin: calc(50% - 50vw)` form
  above resolved it outright.

- **`DxGraph` — a single dynamic entry point over 18 chart kinds, switchable at runtime via a
  `Kind` (`GraphKind`) parameter.** A facade, not a rewrite: every `Kind` case opens the real
  underlying `Dx*Chart` component (`OpenComponent<TComponent>`) and forwards typed parameters —
  zero reflection, zero boxing, and the compiler still catches a typo'd parameter name at the call
  site inside `DxGraph.cs` exactly as it would in hand-written markup. Rebinding `Kind` alone (e.g.
  a toolbar toggling the same series between Bar/Line/Area) re-renders through the matching chart
  with no markup change and no re-binding `Points`.
  Covers exactly the 18 kinds whose data reduces to one of three already-shared, strongly-typed
  shapes: `ChartPoint` (13 kinds: Bar, Area, Line, Pie, Scatter, StackedBar, Radar, Funnel,
  Candlestick, Waterfall, Bubble, Heatmap, Sparkline), a `ChartTreeNode` root (Treemap, Sunburst),
  or a bare scalar/raw-sample list needing no new type at all (the two gauges, Histogram). The
  other 7 chart types (`DxBulletChart`, `DxBoxPlot`, `DxSankeyChart`, `DxNetworkGraph`,
  `DxParallelCoordinates`, `DxWordCloud`, `DxChordDiagram`) each need their own dedicated data
  record that no other kind reuses — folding one into `DxGraph` would cost one new parameter (or
  pair) for exactly one kind, no consolidation benefit, just a wider surface on the shared facade.
  Those 7 stay as their own named components, used directly. `DxGraph` is additive — every
  existing `Dx*Chart` component is unchanged and remains the primary documented API for a known,
  fixed chart type; `DxGraph` is for the dynamic-kind case. Demoed live in `Charts.razor` with a
  Kind-toggle UI, verified in a real browser (including exercising the Rust/wasm compute backend
  through the facade for Line/Area, not just the bUnit managed fallback).

### Changed

- **Unified chart data model — `ChartPoint`, replacing per-chart bespoke shapes (breaking).**
  Every chart that plots a series (`DxBarChart`, `DxPieChart`, `DxFunnelChart`, `DxSparkline`,
  `DxLineChart`, `DxAreaChart`, `DxScatterChart`, `DxStackedBarChart`, `DxRadarChart`,
  `DxCandlestickChart`) now takes one `Points: IReadOnlyList<ChartPoint>` parameter instead of a
  bespoke type per chart (`ChartBar`, parallel `X`/`Y` lists, a bare `Values` list, `ChartSeries`,
  `Candle`). `ChartPoint(X, Y, Category, Y2, Y3, Y4, Series, Color)` is a superset shape — a
  bar/pie/funnel/sparkline chart reads `Category` + `Y`; a line/area/scatter chart reads `X` + `Y`;
  a stacked-bar/radar chart also reads `Series` to group points onto the existing `Categories`/`Axes`
  axis list; a candlestick reads `Y`..`Y4` as Open/High/Low/Close. Unused fields are ignored per
  chart type — a plain record struct, no reflection. `DxHistogram` (raw, unbinned samples) and the
  two gauges (a single scalar `Value`) are unchanged by design — they aren't a plotted point series.
  This is the first step (`ChartPoint` itself) of a planned `[ChartRow]`/`[ChartSeries]` source
  generator for binding an existing domain type onto this shape with zero reflection.

### Added

- **Chart visual language upgrade + four new chart types (the July "Graphs" pass, part 1 of 3).**
  Every chart now draws itself in instead of just appearing: discrete marks (bar/slice/dot/stage/
  candle/vertex) fade-and-rise in with a per-mark stagger, continuous paths (line/area) wipe in
  left-to-right — both a `prefers-reduced-motion`-respecting CSS animation, no new dependency. The
  keyboard-focused mark now gets a soft glow (`drop-shadow`) alongside its existing outline. A new
  opt-in `Gradient` bool (currently on `DxBarChart`/`DxWaterfallChart`) fills a mark with a
  top-to-bottom fade of its own color via a shared `ChartGradients` SVG `<defs>` helper — works
  with any color, no hardcoded shade math, so it stays theme-safe.
  Four new chart types, all following the same progressive-enhancement selection contract as the
  original 10: **`DxWaterfallChart`** (bars float from a running total; a point with `Y2` set is an
  absolute "total" that resets it, with dashed connectors tracing the total across bars);
  **`DxBubbleChart`** (scatter + a third dimension via `Y2`, linearly scaled to a radius range);
  **`DxHeatmap`** (a `Series`×`Category` grid, intensity drawn as `fill-opacity` on the accent
  color — not a hand-rolled color scale, and never the only signal); and **`DxBulletChart`**
  (Stephen Few's KPI-vs-target design, on a new dedicated `BulletPoint`/`BulletPointEventArgs`
  pair — a bullet row's own scale and range bands don't fit the flat `ChartPoint` shape). Demoed
  live in `Charts.razor`.

- **Four hierarchical/statistical/flow chart types (the July "Graphs" pass, part 2 of 3):
  `DxTreemap`, `DxSunburst`, `DxBoxPlot`, `DxSankeyChart`.** These don't fit the flat `ChartPoint`
  shape, so each brings its own data type and a matching headless layout primitive in
  `BlazorDX.Primitives.Charts` (unit-tested independently of any rendering):
  - **`ChartTreeNode`** (a recursive `Label`/`Value`/`Color`/`Children` record) feeds both
    **`DxTreemap`** (squarified layout — `TreemapLayout`, the Bruls/Huizing/van Wijk algorithm, so
    cells stay close to square instead of degenerating into slivers) and **`DxSunburst`** (radial
    partition — `SunburstLayout` — every node draws as its own ring segment, not just leaves).
  - **`BoxPlotGroup`** (a label + raw sample list) feeds **`DxBoxPlot`**: Q1/median/Q3 box,
    whiskers, and outliers beyond 1.5x IQR (`BoxPlotStatistics`, Tukey's convention, pure math over
    an already-sorted sample — sorting itself is offloaded to the existing `IGridCompute.SortAsync`,
    nothing new to duplicate there). A `Violin` bool also draws a density silhouette behind each
    box, binned via the same compute backend as `DxHistogram` over a shared value axis so every
    group's silhouette aligns.
  - **`SankeyNode`/`SankeyLink`** feed **`DxSankeyChart`**: a layered ("Sugiyama-style") layout
    (`SankeyLayout`) — each node's layer is its longest path from a source, nodes stack vertically
    within their layer proportional to total flow, links draw as thickness-scaled bezier ribbons.
    Not full crossing-minimization (that's d3-sankey's iterative relaxation) — a deliberate
    simplification for the node/link counts a Sankey diagram realistically shows.
  Selection on all four is opt-in like the rest of the family, but independently-focusable
  (natural tab order) rather than the flat charts' roving-index pattern — a nested hierarchy or a
  node/link graph doesn't reduce to one linear index the way a bar or slice list does. Demoed live
  in `Charts.razor`.

- **Four more chart types close out the July "Graphs" pass (part 3 of 3):
  `DxNetworkGraph`, `DxParallelCoordinates`, `DxWordCloud`, `DxChordDiagram`.** Every planned chart
  type from the roadmap note is now shipped — 21 chart/gauge/sparkline types total.
  - **`GraphNode`/`GraphEdge`** feed **`DxNetworkGraph`**: a force-directed ("spring embedder",
    Fruchterman-Reingold-style) layout (`ForceDirectedLayout`) — connected nodes cluster, unconnected
    ones drift apart. Deliberately plain C#, not a Rust/wasm kernel: realistic network diagrams run
    to tens or low hundreds of nodes, well within budget even at the algorithm's O(n²)-per-step cost
    — the same "does this need Rust" call this library already makes for the Scheduler's date math
    and the other Tier-2 layouts.
  - **`ParallelCoordinateRow`** feeds **`DxParallelCoordinates`**: one vertical axis per dimension
    (independently min/max-normalized), each row a polyline crossing every axis at its own value —
    the one chart in the family built for spotting clusters/correlations across many dimensions at
    once, something no 2-D chart here shows.
  - **`WordCloudEntry`** feeds **`DxWordCloud`**: spiral-packing layout (`WordCloudLayout`, the
    classic Wordle/d3-cloud approach) — words placed largest-first, spiraling outward until a
    non-overlapping spot is found (an axis-aligned-box approximation of each word's extent, since
    exact glyph metrics aren't available without a font-measurement pass). A word that can't fit is
    dropped, not thrown.
  - **`ChordNode`/`ChordLink`** feed **`DxChordDiagram`**: `ChordLayout` sizes each node's arc by its
    total involvement and slices it proportionally per connection (the same value-to-angle scale
    drives both, so a node's slices always exactly fill its own arc); each link draws as a ribbon —
    two inner-edge arcs joined by quadratic curves through the circle's center.
  Selection follows the same independently-focusable pattern as Tier 2's four. Demoed live in
  `Charts.razor`.

- **`[ChartRow]`/`[ChartValue]` source generator** — bind an existing domain type straight to a
  chart with `rows.ToChartPoints()`, no manual `ChartPoint` construction, no reflection. Tag a class
  or struct `[ChartRow]` and its properties `[ChartValue(ChartField.Category)]` /
  `.X` / `.Y` / `.Y2` / `.Y3` / `.Y4` / `.Series` / `.Color`; `BlazorDX.SourceGen` emits a
  `{Type}ChartExtensions.ToChartPoints()` extension at build time. `Category`/`Series`/`Color`
  accept a property of any type (stringified via `Convert.ToString`, so an `int` or `enum` category
  works as-is); the numeric fields require a numeric-convertible property — tagging a non-numeric
  one is silently not mapped (degrades gracefully, mirroring `[GridColumn]`'s policy) rather than a
  compile error. Mirrors `[GridRow]`/`[GridColumn]`'s zero-reflection story for the DataGrid; this
  closes out the "Data" unification alongside `ChartPoint` above. `Charts.razor` demos it live via a
  `SalesRecord` row type feeding a `DxBarChart`.

- **Chart point selection, hover, and legend-toggle events** — a progressive-enhancement layer on
  the 7 discrete-mark charts (`DxBarChart`, `DxPieChart`, `DxFunnelChart`, `DxScatterChart`,
  `DxStackedBarChart`, `DxRadarChart`, `DxCandlestickChart`). Wiring `OnPointSelected` and/or
  `OnPointHovered` turns a chart into a keyboard-navigable widget — `role="application"`, roving
  `aria-activedescendant`, arrow-key navigation (Home/End, no wrap), Enter/Space to select — the
  same active-cell pattern already used by the DataGrid, Scheduler, and Calendar, generalized to a
  geometry-agnostic roving index (`ChartSelectionPrimitive`, new, in `BlazorDX.Primitives.Charts`).
  With neither event wired, a chart renders exactly as it always has (`role="img"`, no tabindex) —
  fully backward compatible. `DxPieChart`/`DxStackedBarChart`/`DxRadarChart`'s legends are always
  click/keyboard-operable buttons that hide/restore a slice or series and raise `OnLegendToggled`,
  independent of point-level interactivity. `DxLineChart`/`DxAreaChart` (continuous, LTTB-downsampled
  to hundreds/thousands of points) and `DxSparkline` (explicitly decorative) are deliberately out of
  scope for point-level selection — zoom/pan is the right future interaction there, not per-point
  keyboard nav. `Charts.razor` demos both new events live (an interactive bar chart, a toggleable
  pie legend).

- **PeopleHub HRIS example app** (`/hr`) — a six-module HR platform on a Scoped store: a
  **dashboard** (headcount/type/hiring charts + an average-tenure gauge), an employee **directory**
  (`DxDataGrid`), an employee **profile** with a source-generated `DxForm` and tabs, an **org chart**
  (`DxTreeGrid` reporting hierarchy), **time off** (`DxCalendar` with leave markers + an approve/deny
  request queue), and **onboarding** (`DxKanban` of new hires by stage). Showcases DataGrid,
  TreeGrid, Form, Charts, Calendar, and Kanban composed in one app.
- **Two new example apps in the demo.** Joining TicketDesk: **ContentVault** (`/ecm`), an enterprise
  content-management workspace — folder tree, document DataGrid (sort/filter/export), and a detail
  dialog with a source-generated metadata `DxForm`, version history, classification/status badges,
  and check-out/check-in + review/approve/publish lifecycle; and **Mailbox** (`/mail`), a three-pane
  email client — folders with unread badges, message list with search/star/read state, a reading
  pane that renders message bodies through an injected sanitizer, and compose (`DxRichTextEditor` in
  a `DxSheet`). Both use Scoped in-memory stores.

- **`DxCalendar` — inline month calendar.** A standalone, always-visible calendar (distinct from the
  `DxDatePicker` popup) built on a new `CalendarPrimitive`: single or range selection
  (`SelectionMode`, with a range hover preview), `Min`/`Max` bounds, an `IsDateDisabled` predicate,
  a `MarkedDates` dot layer, and a per-day `DayTemplate`. The week starts on the culture's first day;
  it is a real ARIA `grid` with 2-D arrow / Home/End / PageUp/Down (Shift = year) keyboard navigation
  and a polite month live region.
- **Scheduler recurrence (`DxScheduler` / `SchedulerPrimitive`).** `SchedulerEvent` gains an
  optional RRULE-style `Recurrence` rule — `Daily` / `Weekly` / `Monthly` with `Interval`,
  `Count`, `Until`, and weekly `ByWeekday`. Seeds are expanded into concrete occurrences for the
  visible window only (never an unbounded series); `Count` is measured from the seed, so paging
  the view never shifts the dates a rule produces. Pure C#, AOT/trim-safe, bounded by a safety cap.
- **Scheduler drag-to-move / drag-to-create** on the Week/Day time grid. A thin TypeScript pointer
  bridge (`scheduler.ts` via the new `ISchedulerInterop`) snaps the gesture to the day column and
  half-hour, shows a move ghost / create preview, and auto-scrolls near the grid edges; all date
  math, index re-validation, and clamping stay in C# (`ApplyMoveAsync` / `ApplyCreateAsync` raising
  `OnEventMoved` / `OnRangeCreated`). Recurrence occurrences are not directly draggable. Drag is a
  progressive enhancement — keyboard navigation and click-to-select are unchanged, and the server
  uses a no-op bridge.
- **File-upload integrity verification (`DxFileManager`).** Opt-in `VerifyIntegrity` hashes each
  uploaded file in the browser with Web Crypto, then re-hashes the received `IBrowserFile` stream
  and compares, raising a per-file `FileIntegrityResult` via `OnUploadVerified` so corruption in
  transit is caught before the host writes anything. The receiving-side verifier (`FileHasher`) is
  streaming (`IncrementalHash`, never fully buffers) and constant-time; **SHA-256 by default**
  (SHA-1 is supported but never the default — a broken primitive). New client bridge
  `IFileHashInterop` / `file-hash.ts`.

### Security

- **Document-parser hardening (untrusted `.docx`/`.xlsx`).** A review of the document components
  produced fixes for resource-exhaustion (DoS) vectors in the OOXML readers — no XSS/code-exec was
  found (the existing XXE defenses and fail-closed URL allow-lists held):
  - **Spreadsheet column-index amplification (high):** `XlsxReader` now clamps a cell's column to
    Excel's maximum (16384) using overflow-safe math, so a few-byte crafted reference like
    `r="AAAAAA1"` can no longer drive an enormous dense-row pre-pad. Added a per-sheet cell budget.
  - **"Lying" zip-bomb (medium):** part reads are now wrapped in a length-limiting stream that caps
    the bytes *actually* decompressed (not just the declared `ZipArchiveEntry.Length`), closing the
    gap for binary image parts read via `CopyTo`. Added an aggregate image-bytes budget.
  - **Hardening (low):** markdown rejects scheme-relative `//host` links; `data:` image content
    types from untrusted documents are constrained to `image/*`.
  - **CSV/Formula injection on export (CWE-1236):** `DxDataGrid` CSV/TSV export now neutralizes
    cells beginning with `= + - @`, tab, or a line break by prefixing a single quote, so an exported
    file can't execute a formula/command when opened in a spreadsheet. A leading `+`/`-` on a genuine
    number is preserved. New `SanitizeExportFormulas` parameter (default true) opts out for
    non-spreadsheet consumers. (XLSX export was already safe — it writes typed string cells.)

### Fixed

- **Demo:** the Power BI playground sample embed (`/powerbi` in production) returned 502 — the
  upstream playground backend host was retired. Repointed at the playground's current
  `GenerateToken` endpoint and fixed the (now lowercase) JSON key parsing.

## [0.3.7] — 2026-06-26

### Added

- **Undo / redo** in the Word editor (toolbar buttons) — gap fix 6 complete. A
  model-snapshot history captures each edit and each find/replace, so a replace is now
  undoable (fixing the prior history loss). Per-change-event granularity, capped at 200.

## [0.3.6] — 2026-06-26

### Added

- **Find-next navigation** in the Word editor's find bar (‹ ›): selects and scrolls to each
  match in the editor, showing "N of total".
- **Owned-selection primitive** (`richtext.ts` `findInEditor` + `DxRichTextEditor.FindNextAsync`)
  — the first step of [ADR 0015](docs/adr/0015-model-driven-editing-core.md)'s model-driven
  core: the editor selects via the bridge over its live text nodes, no `execCommand`, no
  model↔DOM coordinate mapping. The foundation that unblocks table-editing and full undo/redo.

## [0.3.5] — 2026-06-26

### Added

- **Embedded images** round-trip (gap fix 5): a new `WordImage` block (bytes + content
  type + alt + pixel size). It survives `WordHtml` (base64 `data:` URL `<img>`),
  `DocxWriter`/`DocxReader` (a `word/media` part + image relationship + `<w:drawing>`/
  `pic:pic`), and `DxWordViewer` (`<img>` with an always-present `alt`, WCAG 1.1.1).
  Only base64 `data:` URLs are accepted on parse (no remote `src`). The in-editor
  insert-image affordance (file picker) and images inside tables/lists are deferred.

## [0.3.4] — 2026-06-26

### Added

- **Nested lists** round-trip (gap fix 3): `WordList` gains an optional per-item `Levels`
  array. Nesting survives `WordHtml` (nested `<ul>`/`<ol>` ↔ depth parse), `DocxWriter`/
  `DocxReader` (`<w:ilvl>`, with 4 indented levels declared in numbering.xml so Word
  renders them), and `DxWordViewer` (real nested `<ul>` tree). Existing flat-list callers
  are unaffected (`Levels` null = flat). Per-level ordered/bulleted kind is not modeled.

## [0.3.3] — 2026-06-26

### Added

- **In-editor text color + highlight** swatches on the rich-text toolbar. The bridge
  remembers the last in-editor selection and restores it before applying, so the native
  color picker (which steals the contentEditable selection) still colors the intended
  text. Completes the color gap (round-trip shipped in 0.3.2).

### Docs

- **ADR 0015 — model-driven editing core** (*Proposed*): the decision to invert the editor
  so `WordDocument` is the source of truth and `contentEditable` is an I/O surface (the
  ProseMirror/Lexical pattern), reusing the model + round-trip we already own — no
  `execCommand`, no third-party JS editor. Phased, flag-gated. It unblocks undo/redo,
  table-editing, find-highlight, comments, track changes, and collaboration.

## [0.3.2] — 2026-06-26

### Added — Word editor expansion

- **`DxWordEditor`:** built-in **Download .docx** button, a live **document stats** line
  (word / character / paragraph count), and a model-based **find & replace** bar (match
  count, case toggle, Replace / Replace all).
- **Formatting that now round-trips** to `.docx`: **underline**, **strikethrough**,
  **hyperlinks** (with http/https/mailto URL sanitization and a `.docx` relationship part),
  **paragraph alignment** (`<w:jc>`), and **text color + highlight** (`<w:color>`/`<w:shd>`).
  The rich-text toolbar gains Strikethrough, Insert link, and Align left/center/right/justify.

### Fixed

- **`DxWordViewer` rendered only bold/italic** — it now renders underline, strike,
  hyperlinks, and color/highlight too.
- **Security:** the viewer **scheme-guards `.docx` hyperlink hrefs** (a `.docx` is
  untrusted and the viewer has no sanitizer, so a `javascript:` link from a malicious file
  is dropped rather than rendered clickable).

### Known gaps (tracked)

- In-editor color **apply** UI (round-trip done; the toolbar affordance needs selection
  save/restore). Nested lists, table-editing UI, inline images, undo/redo, and a
  model-driven editing core remain.

## [0.3.1] — 2026-06-26

### Fixed

- **Editable spreadsheet crash:** moving the active cell threw
  `Unexpected frame type during RemoveOldFrame: ElementReferenceCapture` (cascading into
  null-reference / missing-event-handler errors that broke the `excel-edit` page). The
  cell's reference capture is now emitted unconditionally.
- **Spreadsheet footer overlap:** the worksheet tab panel reused the `dx-sheet-panel`
  class owned by the `DxSheet` offcanvas overlay (`position: fixed`), pulling the grid out
  of flow and dropping it on the page footer. Renamed to `dx-sheet-tabpanel`.
- **Blank columns/rows on real `.xlsx`:** `XlsxReader` now trims trailing empty
  rows/columns to the true used range (interior blanks preserved).

### Added

- **Editable spreadsheet, fleshed out:** a **formula bar** (active cell's A1 address +
  raw-content input), a **toolbar** (insert/delete row & column, Download `.xlsx`), and
  Excel-style keyboard entry (type-to-replace, `Delete`/`Backspace` to clear). Structural
  insert/delete does not yet rewrite formula references (documented).

## [0.3.0] — 2026-06-26

### Added — Extended Document Handling track

- **`BlazorDX.Documents`** — `DxSpreadsheetViewer` (Excel `.xlsx` viewer **and**
  editor with a live formula recalculation graph) and `DxWordViewer` / `DxWordEditor`
  (Word `.docx` over a sanitized OOXML↔HTML round-trip). Heavy parsers, quarantined
  in an opt-in package.
- **`BlazorDX.Documents.Parsing`** — UI-free OOXML readers/writers (`XlsxReader`/
  `XlsxWorkbookWriter`, `DocxReader`/`DocxWriter`) and the spreadsheet formula engine
  (tokenizer, parser, evaluator, function library, dependency-graph recalc). Hand-rolled
  on `System.IO.Compression` + `System.Xml` — **no external NuGet dependencies**.
- **`DxDocumentViewer`** (core) — native-embed viewer for PDF and other
  browser-renderable documents; a toolbar + iframe shell, no parser.
- **`BlazorDX.Integrations.Reporting`** — `DxReportViewer`: server-side **SSRS**
  rendering through Microsoft's own URL-access engine, delivered over HTMX (parameter
  forms + fragment swaps, no WASM payload).
- **`BlazorDX.Integrations.PowerBI`** — `DxPowerBiReport`: a thin, lazy-loaded wrapper
  over Microsoft's `powerbi-client` SDK. The embed token is minted server-side and
  fetched from your endpoint, so AAD credentials never reach the browser.
- **`BlazorDX.Htmx`** — `DxHtmxDocumentViewer`: a static-SSR, read-only PDF/Excel/Word
  viewer with a no-JS `href` fallback (zero circuit, zero WASM).
- **`samples/BlazorDX.MockReportServer`** — emulates the documented SSRS URL-access +
  REST and Power BI REST (`GenerateToken`) contracts, so the integrations are verified
  end-to-end against the protocol without live infrastructure.
- Demo pages and accessibility (axe) E2E coverage for every new route; docs:
  `roadmap-documents-and-reporting.md`, ADRs 0010–0014, reporting/Power BI a11y
  statements, and `learn/` entries.

### Changed

- **WCAG 2.2:** every new route is gated by the axe-core E2E suite; reporting/Power BI
  accessibility responsibilities documented (wrapper vs. renderer).
- **CI:** the unit step now also runs the Reporting, Power BI, and MockReportServer
  suites; the AOT publish + smoke job runs on PRs (not only `main`) so a WASM-AOT/trim
  break is caught before merge, not after.
- **Release:** the `Release` workflow no longer publishes to nuget.org with a stored
  API key — it packs and uploads artifacts for **manual** publishing; no release
  credential lives on CI.

### Decided

- **RDLC viewer declined** — we will not ship an unmanaged, vendor-bound renderer; the
  SSRS server-render path covers the reporting need (recorded in ADR-0010).

## [0.2.0] — 2026-06-24

### Added

- WCAG 2.2 **Level A** gap closure across the catalog.
- Versioned release tooling (`Build-Release.ps1`): per-version NuGet packages +
  symbols, a source-snapshot zip, and a SHA-256 manifest.

### Changed

- WCAG 2.2 **AA** hardening: single-pointer + keyboard alternatives for drag
  (2.5.7), 24×24 target sizes (2.5.8), and related fixes extended to sortable/tiles.

## [0.1.0] — 2026-06-21

- Initial beta: the headless two-tier engine; the DataGrid family (flat / tree /
  pivot, server-side data, grouping); data visualization; scheduling; editors; file
  management; forms (one model that doubles as an AI/MCP tool); AI chat; and
  standards-verified barcodes/QR — built to publish trim-clean, zero runtime reflection.

[0.3.0]: https://github.com/logixrcorp/BlazorDX/releases/tag/v0.3.0
[0.2.0]: https://github.com/logixrcorp/BlazorDX/releases/tag/v0.2.0
[0.1.0]: https://github.com/logixrcorp/BlazorDX/releases/tag/v0.1.0
