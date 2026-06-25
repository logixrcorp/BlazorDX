# Roadmap — Extended document type handling

> The **Extended document type handling** track: viewing, editing, and reporting over
> common document types (PDF / Excel / Word), a drag-and-drop file manager, scheduler
> depth, and a functional SSRS / report viewer.
>
> **Scope change.** The main [ROADMAP.md](ROADMAP.md) lists "PDF viewer", "Document
> processing (PDF/Word/Excel parsing)", and "Full in-browser spreadsheet" as *out of
> scope by design*. This track deliberately brings the **viewer/editor** side of those
> into scope, while keeping the **identity guardrails** intact (see below). The
> **report _designer_**, mapping/GIS, and diagram engines remain out of scope.

## Goals (in priority order)

1. **Extremely fast at runtime** — heavy parsing/recalc/layout runs in **Rust → wasm**;
   the DOM is **virtualized**; data crosses the interop boundary **compact** (typed
   arrays / index deltas), never as object graphs.
2. **No bloat in the core** — the trim-clean, zero-reflection, MIT WASM core is never
   weighed down by document engines, large JS SDKs, or external-service dependencies.
3. **Functional reporting using Microsoft's own components** — SSRS reports render via
   Microsoft's supported server APIs / portal, not a reimplementation.

## Identity guardrails (non-negotiable)

- **Separate packages keep the blast radius contained:**
  - **Core `BlazorDX.Components`** holds the **lightweight, dependency-free** doc
    components (file manager, scheduler, native-embed PDF viewer) — pure C# + thin TS, no
    heavy engine, so not "bloat."
  - `BlazorDX.Documents` — MIT, **interactive (WASM)** viewers/editors that **carry a heavy
    engine** (Excel/Word: Rust `.xlsx`/OOXML parse + formula graph). Same rules as core.
    The boundary is **weight/deps, not topic** — see
    [ADR 0010](adr/0010-documents-and-reporting-integration.md).
  - `BlazorDX.Htmx` (the existing static-SSR tier) — the **server-rendered, read-only**
    document viewer and the report viewer's parameter forms + fragment swaps. No WASM
    payload, no SignalR circuit; progressive-enhancement-friendly.
  - `BlazorDX.Integrations.Reporting` — **server-side** SSRS rendering, delivered to
    the browser over the **HTMX/static-SSR tier**. Clearly labeled as integrations; not WASM.
  - `BlazorDX.Integrations.PowerBI` — thin wrapper over Microsoft's `powerbi-client` SDK.
- **Credentials and large JS never reach the browser core.** Report-server creds live in
  the host; heavy JS SDKs (PDF.js, `powerbi-client`) are **lazy-loaded** via dynamic
  `import()` so they never enter the core bundle.
- **Defer to native/remote engines** rather than reimplement them: the browser's PDF
  engine, the SSRS report server, and the Power BI service are faster than anything we
  would ship.

## Accessibility — WCAG 2.2 AA (a process gate, not a phase)

Accessibility has the **same standing as trim-clean publish and the 1000-line cap**: a
component is not "done" until it meets WCAG 2.2 **Level AA**. This track inherits the
mechanisms already in the repo and extends them to every new component.

**Process (applied to every item below):**

- **axe-core in CI** — the existing `AccessibilityE2ETests` (serious/critical = build
  fail, across Chromium/Firefox/WebKit) is extended to **every new demo route**.
- **Tests for what axe can't see** — axe misses target size and missing drag
  alternatives (the exact gaps our last audit hit). Each component adds **target-size
  E2E** (≥24×24, like `TargetSizeE2ETests`) and **keyboard + single-pointer
  drag-alternative** tests.
- **Manual screen-reader pass** (NVDA / JAWS / VoiceOver) per component before "done" —
  the manual half axe cannot cover.
- **Shift-left annotation** — each component's spec/PR documents heading structure, ARIA
  roles, accessible names, focus order, the keyboard model, and `aria-live` regions
  *before* build (WCAG-EM practice).
- **Reuse the established patterns** — 2.5.7 single-pointer reorder, 2.5.8 24×24 hit
  areas, 3.3.1 `aria-invalid`/`aria-describedby`, focus trap/visible, `prefers-reduced-motion`.
- **Accessibility statement for embedded/integration components** — for anything rendered
  by an iframe or a third party (PDF, SSRS, Power BI), the docs state plainly **what
  BlazorDX guarantees** (the wrapper, toolbar, parameter form, names) vs **what depends on
  Microsoft's renderer or the document/report author**, and we always ship an **accessible
  alternative** (download, accessible export, "show as table").

**Per-component WCAG 2.2 focus (the hard parts):**

| Component | Key criteria & the hard part |
|---|---|
| **PDF viewer** | `<embed>`/`<iframe>` needs a `title` (4.1.2); toolbar keyboard-operable, labeled, 24×24 (2.1.1 / 2.5.8); **full conformance depends on a PDF/UA-tagged source** — provide an accessible download, and if we add a custom text layer it must be selectable text (1.1.1). |
| **Read-only doc viewer (HTMX)** | server-renders **semantic HTML** (real headings/tables/reading order) so AT works **without JS** (1.3.1 / 1.3.2); the **no-JS fallback is the conformance floor**; PDF-shell `<embed>`/`<iframe>` `title` (4.1.2); paged HTMX swaps move/announce focus (4.1.3). |
| **Scheduler** | 2-D arrow-key grid navigation; events as buttons with names that include date/time/title (4.1.2); **drag-to-move/create must have a keyboard + single-pointer alternative (2.5.7)**; categories not signaled by color alone (1.4.1); announce view/date changes (4.1.3). |
| **Excel viewer/editor** | reuse grid `role=grid` + cell nav + correct `aria-rowcount`/`aria-colcount` under virtualization (1.3.1); sheet tabs as `tablist` (2.1.1); **formula/validation errors via 3.3.1**; cell state not color-only (1.4.1); column/row drag has an alternative (2.5.7). |
| **Word viewer/editor** | **emit semantic HTML from OOXML** — real headings, lists, table headers, reading order (1.3.1 / 1.3.2 / 2.4.6); carry image alt text from the docx, flag when missing (1.1.1); editor toolbar toggles expose `aria-pressed` and are keyboard-operable. |
| **SSRS viewer** | iframe `title`; **our toolbar + parameter form conform** (3.3.1 / 3.3.2, and 3.3.7 by prefilling last-run parameters); **HTMX delivery keeps a no-JS fallback** (progressive enhancement); prefer the **HTML5 renderer** over legacy HTML; offer an **accessible export** (tagged PDF / data); embedded report-content conformance is Microsoft's/the author's — stated in the a11y statement. |
| **Power BI viewer** | iframe `title`; surface Power BI's own keyboard navigation and **"Show as table"**; embedded conformance is Microsoft's — stated in the a11y statement. |
| **File manager** | **native DnD has a keyboard + single-pointer move alternative (2.5.7)** and a standard file input alongside drop (DnD is an enhancement, never the only path); tree `role`/`aria-level`/`aria-expanded` + keyboard; announce upload/move/delete (4.1.3); 24×24 action targets (2.5.8); selection not color-only (1.4.1). |

> **Honesty about embedded content:** for PDF, SSRS, and Power BI we can make our
> *wrapper* fully conformant, but we cannot retrofit accessibility into a renderer we
> don't own or an untagged document/poorly-authored report. The conformance strategy there
> is: conform the wrapper, prefer accessible render formats, and always provide an
> accessible alternative — documented per component.

## Language allocation (runtime-first)

| Component | C# (model/render/API) | Rust → wasm (hotspots) | TypeScript (DOM/SDK) |
|---|---|---|---|
| PDF viewer (interactive) | host bytes to native `<embed>`; toolbar/state | text-layer **extraction & search** (only if needed) | — (canvas only for custom text layer) |
| **Read-only doc viewer (HTMX)** | **server-rendered HTML fragments** (PDF shell / Excel / Word), **paged via HTMX** | server-side parse (shared with the interactive readers) | **HTMX attributes only — no WASM, no circuit** |
| Calendar/Scheduler | render + virtualization + API | **RRULE expansion + overlap-lane layout** at scale | pointer drag-to-move/create, scroll measure |
| Excel viewer (interactive) | virtualized `DxDataGrid` render; `.xlsx` **write** (existing `XlsxWriter`) | **`.xlsx` parse + formula recalc graph** | file drop |
| Word viewer (interactive) | virtualized render of the layout model; sanitize | **OOXML parse + pagination/layout** at scale | — |
| Office **editors** | edit state, write-back | incremental **formula recalc** / docx round-trip | contentEditable surface (Word) |
| File manager (hybrid) | tree/selection/breadcrumb + virtualized list; **HTMX for nav/listing** | optional: **hashing / thumbnail decode / large-tree scan** | native DnD + File API (interactive) |
| SSRS report viewer | **server** render call → HTML fragment + param form | — | **HTMX swap (static-SSR)**; iframe embed shell also via HTMX |
| Power BI viewer | MSAL token / API (server) | — | **wrap `powerbi-client`** (unavoidable) |

## Render mode per component

The track is **not** uniformly WASM. Each component picks its tier by interaction shape —
server-driven/coarse → static-SSR + HTMX (fast first paint, no WASM/circuit,
progressive-enhancement); compute-heavy/fine-grained → InteractiveWebAssembly (keeps the
Rust kernels + virtualization that deliver the runtime goal).

| Component | Render tier |
|---|---|
| PDF viewer (interactive shell) | InteractiveWebAssembly |
| **Read-only document viewer** | **Static-SSR + HTMX** |
| Excel / Word viewers (interactive) | InteractiveWebAssembly |
| Excel / Word editors | InteractiveWebAssembly |
| Scheduler | InteractiveWebAssembly |
| **File manager** | **Hybrid** — static-SSR + HTMX for nav/listing/breadcrumb; InteractiveWASM/JS for DnD, upload, preview |
| **SSRS / report viewer** | **Static-SSR + HTMX** |
| Power BI viewer | Interactive (iframe + SDK) + server token |

## Developer experience — simple by default, headless when needed

The Microsoft integrations (SSRS / report viewer, Power BI) — and the document viewers —
follow the same **two-tier, progressive-disclosure** philosophy as the rest of BlazorDX:
trivial for the 90% case, fully customizable for the 10%. Four levels of control, each
opt-in; you only reach for the next when you need it. *(APIs below are illustrative of the
intended shape, not final.)*

**Level 1 — zero-config (one DI call + one attribute).**
```csharp
// Program.cs — once
builder.Services.AddBlazorDXReporting(o =>
{
    o.ServerUrl   = "https://reports.contoso.com/ReportServer";
    o.Credentials = ReportCredentials.CurrentWindowsUser;   // or a service account
});
```
```razor
@* embed mode, default toolbar, auth from DI — nothing else needed *@
<DxReportViewer Report="/Sales/Monthly" />
```
Secure **and** accessible defaults are built in (auth required, output sanitized, iframe
`title`, conformant toolbar). Power BI mirrors it: `AddBlazorDXPowerBi(...)` +
`<DxPowerBiReport WorkspaceId="…" ReportId="…" />`. The document viewers too:
`<DxDocumentViewer Source="…" />`.

**Level 2 — configure (parameters + options).** Per-instance overrides for the common knobs:
```razor
<DxReportViewer Report="/Sales/Monthly"
                Mode="ReportRenderMode.Render"        @* native chrome vs default embed *@
                Format="ReportFormat.Html5"
                Parameters="@reportParams"
                OnRendered="OnRendered" OnError="OnError" />
```
Startup `options` set defaults (format, caching, timeout, toolbar buttons); attributes
override per instance.

**Level 3 — template (RenderFragment slots).** Replace markup without touching behavior:
```razor
<DxReportViewer Report="/Sales/Monthly">
    <Toolbar>…custom toolbar…</Toolbar>
    <ParametersTemplate Context="p">…custom UI…</ParametersTemplate>  @* omit → auto from [DxFormModel] *@
    <LoadingTemplate>…</LoadingTemplate>
    <ErrorTemplate Context="err">…</ErrorTemplate>
</DxReportViewer>
```

**Level 4 — extend / headless (DI-swappable seams + the primitive).** Every moving part is
an **interface resolved through DI (no reflection)**, and the Tier-1 primitive exposes the
raw state for a bespoke shell:
```csharp
builder.Services.AddBlazorDXReporting()
    .UseRenderer<MyRenderer>()               // IReportRenderer  (swap the SSRS engine / API)
    .UseAuth<MyAuthProvider>()               // IReportAuthProvider / ITokenProvider (Power BI)
    .UseCache<RedisReportCache>()            // IReportCache
    .AddRequestInterceptor<TenantHeader>();  // IReportRequestInterceptor (mutate request/headers)
```
`ReportViewerPrimitive` (Tier 1) hands back the rendered stream + state — the same "drop to
the primitive" escape hatch the rest of the catalog offers.

**The rule that keeps both true:** defaults are **secure and WCAG-AA by construction**, and
customization is **additive** — overriding the toolbar, parameter UI, or renderer can't
remove the accessibility/security floor (sanitization, iframe `title`, auth, keyboard +
labels stay enforced by the **primitive**, not the swappable template). This is also why it
warrants **ADR-0014 (layered DX / progressive-disclosure API for integrations)**.

## Cross-cutting performance workstream (Phase 0)

Enables the "extremely fast at runtime" goal for every component below. These trade size
for speed by design — applied to the compute crate and perf-sensitive components only.

- **Speed profile for `BlazorDX.Compute.Rust`**: a `[profile.release]` variant at
  `opt-level = 3` (today it's `"z"` = smallest) + **wasm SIMD**
  (`-C target-feature=+simd128`) for the numeric/parse kernels.
- **Benchmark** the existing grid kernels (sort/filter/aggregate, 100k rows) `"z"` vs
  speed profile, so the speed/size delta is a measured number; record in an ADR.
- **AOT-by-default option** for perf-sensitive deployments (`-p:EnableAot=true` already
  exists + CI `aot-publish`); document the download-size tradeoff.
- **Compact-marshaling helpers** (typed-array spans across `[JSImport]`) and reuse of
  `DxVirtualize<T>` so render cost stays O(visible), not O(rows).

---

## Phases

### Phase 1 — Quick wins (extend what exists)

| Item | Build | Effort |
|---|---|---|
| **Drag-and-drop file manager (hybrid)** | **Static-SSR + HTMX** for navigation / listing / breadcrumb (server returns folder fragments — fast, progressive-enhancement, no WASM); **interactive WASM/JS** for DnD, drop-to-upload, multi-select, and preview — with the **accessible non-drag fallback** (WCAG 2.5.7) and a standard file input. Virtualized list; Rust hashing optional. | S–M |
| **Scheduler depth** | Month/day views, drag-to-move/create, **RRULE recurrence**; recurrence expansion + overlap-lane layout in **Rust** at scale; virtualized time window. | S–M |
| **PDF viewer hardening** | Keep native `<embed>`; add page-nav/zoom/download/print toolbar; parameterized `DxDocumentViewer`. Text-layer/search deferred (Rust extraction) unless required. | S |

**Acceptance:** trim-clean publish; bUnit + Playwright E2E (incl. the DnD no-JS / keyboard
path); **WCAG 2.2 AA gate** (axe clean + target-size + drag-alternative + manual SR pass).

**Status (Phase 1 — shipped on `feat/extended-document-handling`):**

- ✅ **File manager** — interactive DnD + drop-to-upload + InputFile + 2.5.7 keyboard move
  + robustness (race guard, name-collision, bounds, focus, dropped-metadata validation).
  ⏳ *Deferred:* the **HTMX/static-SSR nav-listing half** of the hybrid, and Rust hashing.
- ✅ **Scheduler** — Week/Month/Day, 2-D keyboard nav (incl. PageUp/Down), multi-day &
  midnight events, ARIA grid/application roles. ⏳ *Deferred (marked in code):*
  drag-to-move/create, RRULE recurrence, Rust overlap-lane kernel (layout is C#).
- ✅ **PDF viewer** — native engine + accessible toolbar + `Source` XSS allowlist + embed
  `title`/download fallback. Page-nav/zoom delegated to the native viewer.
- ✅ **Cross-cutting** — axe gate wired to `/files` `/scheduler` `/docviewer` (**12/12 axe
  routes green**; 5 real violations caught + fixed); CI runs `cargo test` + a `RustSpeed`
  build smoke; 543 unit tests green; ADRs 0010–0014 written; manual-SR checklist added.
- ✅ **Closed this pass:** native-DnD / keyboard-move **Playwright E2E** (3 tests, green in
  Chromium); `docs/learn` entries for the 3 components; **ADRs 0010–0014**; the
  **package-home decision** — [ADR 0010](adr/0010-documents-and-reporting-integration.md)
  resolves that the lightweight Phase-1 components correctly live in core
  (`BlazorDX.Documents` is created when the heavy Excel viewer needs it, Phase 2).
- ⬜ **Only remaining for 100%:** the **manual screen-reader pass** (a human task; checklist
  at [docs/accessibility-screen-reader-checklist.md](accessibility-screen-reader-checklist.md)).

### Phase 2 — Document viewers (read-only): interactive + static-SSR

Two delivery tiers over the **same server-side parsers**: an interactive (WASM) viewer for
in-app, fast-scroll use, and a static-SSR + HTMX viewer for server-driven, no-WASM,
progressive-enhancement use.

| Item | Build | Effort |
|---|---|---|
| **Excel viewer (interactive)** | Rust `.xlsx` parse (e.g. `calamine`) → **virtualized `DxDataGrid`**, multi-sheet, formats; marshal cell **windows**, not whole sheets. Reuses the grid + existing export. | M |
| **Word viewer (interactive)** | Rust OOXML parse + layout/pagination model → virtualized C# render; raw HTML routed through the `BlazorDX.Security` sanitizer. | M |
| **Static-SSR read-only viewer (`DxHtmxDocumentViewer`)** | Server-side, **non-editable** rendering delivered as **HTMX fragments** — the **PDF shell** (`<embed>`/`<iframe>` + static toolbar) and **Excel / Word → sanitized semantic HTML** (paged via HTMX). Reuses the same parsers server-side; **no WASM, no circuit**. Lives in `BlazorDX.Htmx`. | M |

**Acceptance:** perf budgets met (e.g. 100k-row workbook / multi-hundred-page doc scroll
stays smooth); trim-clean; tests; no managed reflection; **WCAG 2.2 AA gate** — grid
semantics with correct `aria-rowcount`/`colcount` under virtualization (Excel), and
semantic HTML (headings/lists/table headers/reading order) out of OOXML (Word); the
**static-SSR viewer works with JavaScript disabled** (no-JS fallback).

**Status (Phase 2 — in progress):**

- ✅ **Excel viewer** — the new **`BlazorDX.Documents`** package (per [ADR 0010](adr/0010-documents-and-reporting-integration.md));
  a hand-rolled C# `.xlsx` reader (`XlsxReader`, round-trip-verified against `XlsxWriter`,
  no external deps) feeding `DxSpreadsheetViewer` — multi-sheet `tablist`, virtualized via
  `DxVirtualize<T>` (not `DxDataGrid`, since columns are runtime-dynamic — ADR-0009),
  `role="grid"` with full `aria-rowcount`/`colcount`. axe `/excel` clean; 13 tests.
  ⏳ *Deferred (noted in code):* formulas show their cached value (no recompute), number
  formats / styling / merged cells not interpreted, and the **Rust `calamine` reader**
  (drops in behind the same model when scale demands).
- ✅ **Word viewer** — `DocxReader` (hand-rolled WordprocessingML parse, no external deps)
  → `DxWordViewer`, which renders **directly to semantic elements** (`<h1>`–`<h6>`, lists,
  `<table>` with `<th scope>`) via the render tree — no `MarkupString`, auto-encoded, so
  heading hierarchy/reading order are real (1.3.1/1.3.2/2.4.6). axe `/word` clean; 16 tests.
  ⏳ *Deferred:* images, footnotes, hyperlinks (text only), nested-list levels, merged cells.
- ✅ **Static-SSR `DxHtmxDocumentViewer`** — in `BlazorDX.Htmx`, no WASM/circuit. The
  parsers were first extracted into a UI-free **`BlazorDX.Documents.Parsing`** package
  (resolving the ADR-0010 boundary) so the thin HTMX tier reuses them without dragging in
  the catalog. Renders Word/Excel as semantic HTML + PDF embed shell; sheet/page nav uses
  **dual `href` + `hx-get`** so it works fully **with JavaScript disabled** (verified by
  curl). axe `/htmx/doc` (+ word/pdf variants) clean.
- ✅ **Bug fixed:** scheduler time-grid events failed color-contrast for some event colours
  (white-on-colour) — now color-independent (neutral body + accent stripe). **17/17 axe
  routes green.**

**Phase 2 is complete.** ⏳ Only the standing track-wide item remains: the **manual
screen-reader pass** ([checklist](accessibility-screen-reader-checklist.md)).

### Phase 3 — Office editors (larger; may land post-1.0)

| Item | Build | Effort |
|---|---|---|
| **Excel editor + formula engine** | Rust **dependency-graph incremental recalc**; edits via existing zero-reflection write-back; `.xlsx` write via existing `XlsxWriter`; return changed-cell **deltas**. | L |
| **Word editor** | `DxRichTextEditor` surface + **docx round-trip** (C#/Rust); sanitize on import/export. | L |

**Acceptance:** round-trip fidelity tests; recalc correctness vs reference; trim-clean;
**WCAG 2.2 AA gate** — editing is fully keyboard-operable, cell/formula errors surface via
3.3.1, and editor toolbar toggles expose `aria-pressed`.

### Phase 4 — Reporting integrations (server-side, Microsoft components)

> **Functional via Microsoft's own components.** No official ReportViewer exists for
> Blazor/.NET 10, so the report is rendered by **SSRS itself** (Microsoft-supported APIs)
> and either embedded or displayed.

**`DxReportViewer` (SSRS)** — `BlazorDX.Integrations.Reporting`, delivered over the
**static-SSR + HTMX tier** (no WASM payload, no SignalR circuit). Reporting is inherently
request → render → display, which is exactly HTMX's sweet spot; the heavy work was always
going to be server-side, so HTMX adds no interaction-latency penalty here while removing the
client runtime entirely. Needs server access to the report server.

- **Embed mode (default, most "Microsoft"):** a static-SSR shell hosts an `<iframe>` of the
  Report Server portal / URL-Access viewer
  (`/ReportServer?…&rs:Command=Render&rc:Toolbar=true&<params>`). Microsoft's own toolbar,
  parameter UI, paging, and export — zero reimplementation, fully functional. HTMX re-swaps
  the shell when the surrounding parameters change.
- **Render mode (native chrome):** the **parameter form posts via HTMX** (built on the
  existing `DxHtmxForm` / `[DxFormModel]` story); the host calls a Microsoft SSRS API
  — **URL Access** render-to-format, the **`ReportExecution2005` SOAP** API, or the
  **REST v2.0** API (SSRS 2017+) — to produce **PDF / HTML5 / Excel / image**, and the
  **rendered result is swapped into the page as an HTMX fragment**:
  - PDF → `<embed>` (native viewer),
  - HTML5 → sanitized container,
  - with a static toolbar (format, page nav, print, export). No client WASM.
- **Auth:** pass-through (Windows/Negotiate/Basic or a service account) handled in the
  host; **no credentials in the browser**. Caching of rendered output for repeat views.

**`DxLocalReportViewer` (RDLC) — DECLINED / out of scope.** RDLC ("local report") rendering
on .NET 10 has **no official Microsoft renderer** (the ReportViewer control was never ported
past .NET Framework); the only options are **unofficial community ports** that redistribute
Microsoft's rendering assemblies. Taking such a dependency would undercut the project's
managed/auditable identity (an unmanaged, third-party-repackaged vendor library) and, unlike
SSRS's documented URL-Access HTTP protocol, the in-process RDLC engine **can't be
contract-mocked/verified** the clean way the rest of this track was. **Decision: not
implemented.** Teams needing local reports should use the SSRS viewer against a report
server, or render to PDF out-of-band and show it with `DxDocumentViewer`.

**Power BI viewer** — `BlazorDX.Integrations.PowerBI`: TS wrapper over Microsoft's
`powerbi-client` SDK (**lazy-loaded**) + C# **MSAL** token acquisition. Requires Azure AD +
**paid Power BI Embedded capacity** — documented as an external/paid dependency.

**Acceptance:** works against a real SSRS instance end-to-end (embed + render modes);
documented setup; creds never marshaled to WASM; the heavy JS SDK never enters the core
bundle; **WCAG 2.2 AA gate** — iframe carries a `title`, our toolbar/parameter form
conform (3.3.1 / 3.3.2, prefill for 3.3.7), the HTML5 renderer is preferred, an accessible
export is offered, and an **accessibility statement** delineates wrapper vs renderer
responsibility.

**Status (Phase 4 — complete to the verifiable boundary):**

- ✅ **SSRS report viewer** (`BlazorDX.Integrations.Reporting`) — URL-Access client +
  `DxReportViewer` (embed + render modes, parameter form, no-JS), **validated end-to-end
  against a mock SSRS server** that faithfully emulates the URL-Access HTTP protocol
  (`samples/BlazorDX.MockReportServer`); hardened against URL/path injection. axe `/reports`
  clean. ⏳ *Unverified by design:* against a live production SSRS instance.
- ✅ **Power BI viewer** (`BlazorDX.Integrations.PowerBI`) — embed-token service (the
  documented `GET report` → `POST GenerateToken` REST flow, AAD token server-side only) +
  `DxPowerBiReport`, **validated against a mock Power BI REST + a stub SDK** proving the full
  embed loop (mock → embed token → client → `powerbi.embed`). axe `/powerbi` clean.
  ⏳ *Unverified by design:* the real `powerbi-client` render against a live Azure tenant.
- ❌ **RDLC** — **declined** (no official .NET renderer; only unmanaged third-party ports —
  see the `DxLocalReportViewer` note above).

**Phase 4 is complete** (SSRS + Power BI shipped and contract-verified; RDLC declined).

---

## Sequencing & dependencies

```
Phase 0 (perf foundation)  ─┬─►  Phase 1 (file mgr, scheduler, PDF)      [quick wins]
                            ├─►  Phase 2 (Excel/Word viewers)           [Rust readers]
                            │        └─►  Phase 3 (editors)             [larger]
                            └─►  Phase 4 (SSRS + Power BI)             [server integrations]
```

- **Phase 0** lands first (or alongside Phase 1) — it's the lever for the runtime goal and
  is reused by Phases 1–3.
- **Phase 1** ships fast and proves the UX on existing primitives.
- **Phase 4** is independent of 1–3 and can run in parallel; it touches server code, not
  the WASM core.

## Deliverables checklist (per component)

- [ ] **Render mode chosen deliberately** (see *Render mode per component*): static-SSR +
      HTMX for server-driven/read-only/coarse-interaction; InteractiveWebAssembly for
      compute-heavy/fine-grained interaction; hybrid where both apply.
- [ ] Tier-1 primitive (headless behavior + ARIA) and Tier-2 styled wrapper.
- [ ] Rust kernel **with a managed C# twin** where a kernel exists (SSR/fallback parity).
- [ ] Demo page + in-app source view + a `docs/learn` "Concept → Code → Why" entry.
- [ ] bUnit + Playwright E2E + (Rust) `cargo test`; trim-clean publish.
- [ ] **WCAG 2.2 AA gate** — axe-core clean on the new route; target-size E2E (≥24×24);
      keyboard + single-pointer drag alternative where drag exists; `aria-live` for async
      states; iframe `title` for embeds; manual screen-reader pass recorded.
- [ ] **Accessibility statement** (embedded/integration components only): what we
      guarantee vs the renderer/author, plus the accessible alternative offered.
- [ ] ADR for any architectural decision (e.g. **ADR-0010 documents-and-reporting
      integration policy**, **ADR-0011 speed-vs-size Rust profile**,
      **ADR-0012 WCAG 2.2 AA conformance gate for the documents/reporting track**,
      **ADR-0013 render-mode / HTMX-vs-WASM selection policy**,
      **ADR-0014 layered DX / progressive-disclosure API for integrations**).
