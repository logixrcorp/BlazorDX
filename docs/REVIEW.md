# Reviewing BlazorDX — a guide for the senior reviewer

This document exists so your time goes to **judgment, not archaeology**. BlazorDX
makes a handful of strong claims; below, each one is paired with where it lives, how
to verify it, and what would *falsify* it. Please try to break them — a review that
only confirms what we believe is worth little.

The library is young (v0.4.4, single author, no production deployments yet). We are
not asking "is this as mature as Telerik" — it isn't. We are asking: **is the
architecture sound, are the differentiating claims actually true across the whole
catalog (not just the demo), and is the public surface safe to freeze at 1.0?**

---

## How to use this guide

Review in two stages — deep on the load-bearing 20%, broad on the rest:

1. **Foundations (do this first, deeply).** The ADRs, the engine primitives, the
   source generator, the analyzers, the interop boundary, and one full vertical
   slice (the DataGrid). If these are sound, the 108 leaf components built on them
   inherit that soundness.
2. **Breadth (later, broadly).** Spot-check the leaf components and audit the public
   API surface for consistency before 1.0.

### 30-minute orientation

Read, in order: [`docs/adr/`](adr) (0001–0009, one decision each),
[`ARCHITECTURE.md`](ARCHITECTURE.md), [`COMPONENTS.md`](COMPONENTS.md), then this file.

### Repo map

| Path | What it is |
|---|---|
| `src/BlazorDX.Primitives` | Tier-1 headless behavior/ARIA (no CSS). The engine. |
| `src/BlazorDX.Components` | Tier-2 styled `Dx*` wrappers (RenderTreeBuilder + CSS vars). |
| `src/BlazorDX.Interop` (+ `.Ts`) | `[JSImport]`/`[JSExport]` bridges; TypeScript → minified ESM. |
| `src/BlazorDX.Compute` (+ `.Rust`) | C# façade + `dx_grid` Rust crate → wasm32, with managed fallback. |
| `src/BlazorDX.SourceGen` | Roslyn generator: `[GridRow]`/`[GridColumn]` → `IGridRowAccessor<T>`. |
| `src/BlazorDX.Security` | HTML sanitizer + scoped-state helpers. |
| `src/BlazorDX.Htmx` | Static-SSR, no-JS-fallback document/report viewers over htmx. |
| `src/BlazorDX.Documents` (+ `.Parsing`) | Hand-rolled OOXML (.xlsx/.docx) viewer/editor + parsers; no external deps. |
| `src/BlazorDX.Integrations.PowerBI` | Embed-token minting + `powerbi-client` wrapper; credentials stay server-side. |
| `src/BlazorDX.Integrations.Reporting` | SSRS rendering via Microsoft's URL-access engine. |
| `analyzers/BlazorDX.Analyzers` | DX1000/1001/1002 governance analyzers. |
| `samples/BlazorDX.Demo` | Blazor Web App (all render modes) hosting every component. |
| `tests/` | bUnit (components/primitives), compute, analyzer, integration, and Playwright E2E tests. |

Build everything and run the suite first:

```bash
dotnet build BlazorDX.slnx -c Release      # expect 0 warnings / 0 errors
dotnet test  BlazorDX.slnx -c Debug        # expect all green (~950 tests)
cd src/BlazorDX.Compute.Rust && cargo test # Rust kernel unit tests
```

If the build is not clean on your machine, **stop and tell us** — warnings-as-errors
means a warning is an untriaged defect, and a dirty build invalidates the rest.

---

## The claims, and how to break them

Each claim lists: **where** it lives · **verify** (do this) · **falsified if** · **severity**.

### 1. Zero runtime reflection

- **Where:** `BlazorDX.SourceGen` (grid binding), `[JSImport]` interop (no `IJSRuntime`),
  JSON via source-gen contexts.
- **Verify:** grep the shipping libraries for reflection and dynamic dispatch:
  ```bash
  grep -rnE "System\.Reflection|GetType\(\)\.(GetProperty|GetField|GetMethod|GetMembers)|Activator\.CreateInstance|MakeGenericType|PropertyInfo|\bdynamic\b|Type\.GetType" src/
  ```
  Then read the generated accessor: build the demo and inspect
  `obj/.../generated/BlazorDX.SourceGen/.../*GridAccessor.g.cs` — confirm cell
  read/write is a `switch` over members, never `PropertyInfo`.
- **Falsified if:** any reflection or `dynamic` on a hot path in the shipping libs
  (test code may use it freely), or the generator falls back to reflection for any
  column type.
- **Severity:** Critical — this is a core differentiator.

### 2. AOT / trim-clean

- **Where:** `Directory.Build.targets` sets `IsTrimmable`/`IsAotCompatible` on the
  libraries; `Directory.Build.props` sets `TreatWarningsAsErrors`.
- **Verify:** publish a **consumer app** (not just the libraries) and watch for
  IL trim/AOT analyzer warnings:
  ```bash
  dotnet publish samples/BlazorDX.Demo/BlazorDX.Demo.Client -c Release
  # expect: no IL2xxx (trim) or IL3xxx (AOT) warnings; ILLink runs ("Optimizing assemblies for size")
  ```
  For a true native compile (stronger), install the workload and turn on AOT:
  ```bash
  dotnet workload install wasm-tools
  dotnet publish samples/BlazorDX.Demo/BlazorDX.Demo.Client -c Release -p:RunAOTCompilation=true
  ```
- **Falsified if:** any IL2xxx/IL3xxx warning surfaces from BlazorDX assemblies, or a
  component throws at runtime under a trimmed/AOT publish that works untrimmed.
- **Severity:** Critical — the whole positioning rests on this. The wider ecosystem
  struggles here (see dotnet/aspnetcore#64802), so this is the claim most worth
  attacking.

### 3. Headless two-tier

- **Where:** every component is a `*Primitive` (in `Primitives`) + a `Dx*` styled
  wrapper (in `Components`).
- **Verify:** pick three components (e.g. `DxSelect`, `DxDialog`, `DxDataGrid`).
  Confirm the primitive contains no CSS and no hardcoded design, that behavior/ARIA
  lives in the primitive, and that the styled layer themes purely through CSS
  variables (grep `var(--dx-` in `wwwroot/*.css`; there should be no `!important`).
- **Falsified if:** behavior leaks into the styled layer, or styling can't be
  overridden without fighting specificity / forking the component.
- **Severity:** High — "restyle anything" is a headline promise.

### 4. Governance enforced by tooling (not honor system)

- **Where:** `analyzers/BlazorDX.Analyzers` (DX1000 line cap, DX1001 `MarkupString`
  ban, DX1002 singleton-UI-state ban) + `build/FileLength.targets` for `.rs/.ts/.css`.
- **Verify:** the analyzer tests assert each rule fires (`tests/BlazorDX.Analyzers.Tests`).
  Independently, break a rule and confirm the **build fails**:
  ```bash
  # append 1001 lines to any .cs file, or add `new MarkupString(userInput)` outside
  # the audited sanitizer boundary, then: dotnet build  → expect DX1000 / DX1001 error
  ```
- **Falsified if:** a violation builds clean, or the rules are advisory (warning, not
  error) anywhere.
- **Severity:** Medium — it's a process guarantee, not user-facing, but it's load-bearing
  for the "stays auditable" story.

### 5. Security baseline (sanitizer-only raw HTML)

- **Where:** `src/BlazorDX.Security` (sanitizer); the only sanctioned `MarkupString`
  sites; `DxMarkdown` (encode-then-format) and `DxRichTextEditor` (consumer-injected
  sanitizer).
- **Verify:** find every raw-HTML boundary and confirm each is sanitized:
  ```bash
  grep -rnE "new MarkupString|#pragma warning disable DX1001" src/
  ```
  Then attack: paste `<script>`, `javascript:` links, and `on*=` attributes into the
  Markdown and rich-text demos and confirm they render inert. Check interop entry
  points validate/clamp their inputs.
- **Falsified if:** any unsanitized raw-HTML path reaches the DOM, or XSS survives the
  Markdown/rich-text pipeline.
- **Severity:** Critical.

### 6. Accessibility

- **Where:** ARIA roles + keyboard nav live in the primitives (roving-tabindex,
  focus trap, aria-activedescendant patterns).
- **Verify:** this is the claim we are **least** able to self-attest, and where your
  experience is most valuable. Run a real screen reader (NVDA/VoiceOver) and a
  keyboard-only pass over: DataGrid, Select/ComboBox/Listbox, Dialog/Sheet, Menu,
  DatePicker, Tabs. (axe-core already runs in CI — `AccessibilityE2ETests`, across
  Chromium/Firefox/WebKit, over the showcase and the TicketDesk demo, with zero
  serious/critical violations — so spend your time on the manual screen-reader and
  keyboard passes it can't do.) Check focus management on open/close, `aria-*`
  correctness, and that nothing is mouse-only.
- **Falsified if:** keyboard traps, missing/incorrect ARIA, unmanaged focus, or
  components unusable with a screen reader.
- **Severity:** High — currently self-graded **B**; an external audit is exactly what
  we want from you.

### 7. Compute correctness (Rust kernels + barcodes/QR)

- **Where:** `dx_grid` (sort/filter/aggregate/histogram/LTTB) with a managed C#
  parity path; `src/BlazorDX.Primitives/Barcodes` (EAN-13, Code 128, QR).
- **Verify:** `cargo test` for the kernels; the C# tests assert managed↔Rust parity.
  The barcode/QR encoders are anchored to **published reference vectors** (EAN-13
  `5901234123457`; Code 128 checksum `PJJ123C → 54`; QR "HELLO WORLD" 1-M data + EC
  codewords) and QR round-trips through an independent reader. Re-derive a vector by
  hand or with an external tool and confirm.
- **Falsified if:** managed and Rust paths disagree, or any encoder diverges from the
  published standard.
- **Severity:** High for the grid kernels; Medium for codes.

### 8. Test quality (not just count)

- **Where:** `tests/` (~950 tests).
- **Verify:** sample a dozen tests across families. Are they asserting *behavior and
  edge cases* (empty data, boundary values, escaping, keyboard edges, data-identity
  changes), or just "it rendered"? Check the source generator and analyzer tests in
  particular.
- **Falsified if:** tests are predominantly shallow render-smoke checks, or critical
  paths (write-back, masking, sanitization) are untested.
- **Severity:** Medium.

### 9. API ergonomics & 1.0 readiness

- **Where:** the public surface of every `Dx*` component and primitive.
- **Verify:** judge naming/parameter/binding consistency across the catalog — are
  `@bind-Value`/`@bind-Open`/`Items`/`OnX` conventions uniform? Are there leaky
  abstractions or footguns that should change *before* SemVer locks them?
- **Falsified if:** inconsistent conventions, or public API that will force a breaking
  change soon after 1.0.
- **Severity:** Medium-High — cheap to fix now, expensive after 1.0.

---

## Known risk areas (our honest self-disclosure)

We'd rather flag these than have you discover them and wonder what else we hid:

- **No production track record.** Zero real deployments. No amount of review fixes
  this; it needs time and users. Treat correctness, not battle-testing, as the bar.
- **Accessibility is unaudited.** Built to the ARIA patterns, never formally tested
  with assistive tech. This is the biggest credibility gap.
- **Chart interactivity is partial.** Point selection, hover, and legend toggling shipped
  (`title`-tag tooltips only, not a rich hover card); zoom/pan is still open.
- **Server-side grid data binding has shipped** (`IGridDataSource`/`IGridGroupDataSource`,
  with server-side grouping + aggregation) — no longer in-memory-only, but it's newer and less
  exercised than the in-memory path; treat it as the less-battle-tested half of the DataGrid.
- **Native WASM AOT is verified as trim-clean, but full `RunAOTCompilation` is
  exercised less than IL-trimming** — please run it (see claim 2).
- **QR is verified by anchored codewords + independent readback + structure, not by a
  byte-compare against a published 21×21 matrix or a hardware scanner.** Scanning a
  rendered symbol with a phone would be a welcome extra check.
- **Single author.** No second committer has yet exercised the contribution rails.

## Out of scope by design (please don't grade these as gaps)

These are deliberately excluded — they're separate products (server-side document
engines, not UI components) and pursuing them would dilute the headless/auditable
identity: **document processing (PDF/Word/Excel generation & parsing), report
designer, full in-browser spreadsheet, PDF viewer, mapping/GIS, Outlook-depth
recurring scheduler.** See [ROADMAP.md](ROADMAP.md) for the scoped definition of
"complete."

---

## Filing findings

Please bucket each finding by severity (Critical / High / Medium / Low) and, where
possible, point at a file:line and the claim it touches. Critical findings against
claims 1, 2, or 5 are the ones we most want to hear about — those are the bets the
whole project rests on.
