# Changelog

All notable changes to BlazorDX are documented here. The format is loosely based on
[Keep a Changelog](https://keepachangelog.com/), and the project aims to follow
[Semantic Versioning](https://semver.org/) once it reaches 1.0.

> **Beta.** BlazorDX is pre-1.0 and built with substantial AI assistance. Breaking
> changes can land in any minor release until 1.0.

## [Unreleased]

### Added

- **`DxLineChart`/`DxAreaChart`: zoom/pan, opt in via `Zoomable`.** The roadmap's one remaining
  "Chart interactivity" item — point selection, hover, and legend toggling had already shipped
  for the discrete-mark charts, but `ChartSelectionPrimitive`'s own doc comment had always
  excluded line/area precisely because they're "better served by a future zoom/pan interaction."
  This is that interaction. Wheel to zoom (center-anchored), drag to pan, or use the keyboard
  (arrows pan, `+`/`-`/Ctrl+ArrowUp/Down zoom, Home/`0` reset) — a reset button appears in the
  chart's caption only while zoomed, and double-click also resets. Full design record in
  [ADR 0017](docs/adr/0017-chart-zoom-pan-strategy.md); the short version:
  - Both charts already set `preserveAspectRatio="none"` and `vector-effect="non-scaling-stroke"`
    on their SVG — unused before this, but exactly what a **viewBox crop** needs. Zoom crops the
    `viewBox`'s X extent to the visible domain window; the existing point-projection math
    (`BuildPoints()`/`BuildPaths()`) is completely unchanged, still computed against the full
    data domain. At full zoom-out the dynamic `viewBox` reduces exactly to the original static
    string.
  - Zoom never re-runs LTTB downsampling for the zoomed range — it re-projects the
    already-downsampled point set. Zooming in past the initial downsample resolution reveals no
    new real data points; re-downsampling mid-gesture would mean an async Rust/wasm round-trip
    with real debounce/race surface this codebase has no precedent for.
  - X-axis only — Y always covers the full dataset's range, matching how time-series zoom
    conventionally works and avoiding `DxAreaChart`'s baseline-closing math needing a filtered Y
    range.
  - Wheel-zoom is center-anchored, not cursor-anchored: cursor-anchoring would need an async JS
    interop call (the SVG's real rendered width — `dx-chart.css` makes it `width: 100%`, so the
    `Width` parameter doesn't reflect the actual on-screen size) on **every wheel tick**, which
    would be the first hot, continuous-input path in this codebase to do an async round-trip per
    event. Drag-pan does need that same width measurement (there's no way around it for a
    correct pan speed), but only once per gesture, at `onpointerdown` — cached and reused
    synchronously for the rest of the drag. A new `IChartZoomInterop`/`ChartZoomInterop`/
    `NullChartZoomInterop` pair carries just that one capability; if the measurement comes back 0
    (server/prerender, or the element isn't in the DOM yet), the pan gesture simply doesn't start
    for that attempt.
  - `Zoomable` defaults to `false` — the existing chart-family convention gates interactivity on
    "has the host wired a callback," which zoom/pan has no equivalent of (it's self-contained
    view state, not something a host necessarily reacts to). Defaulting it on would have silently
    hijacked the page's mouse-wheel scroll gesture on every existing line/area chart.
  - New Tier-1 primitive `ChartZoomPrimitive` (`src/BlazorDX.Primitives/Charts/`) mirrors
    `ChartSelectionPrimitive`'s shape: pure C# state, geometry-agnostic (real X units for the
    line chart, point-index units for the area chart, which lays out by index).
  - `DxGraph`'s `Line`/`Area` kinds needed explicit new `Zoomable`/`OnZoomChanged` plumbing —
    parameters forward via individually-named calls per kind there, not generically.

- **`DxScatterChart`/`DxBubbleChart`: rectangular (both-axes) zoom/pan.** Closes the "Chart
  interactivity" roadmap item's remaining "other continuous-domain chart kinds" line. Unlike
  `DxLineChart`/`DxAreaChart` (X-only — Y always covers the full dataset), scatter/bubble are
  genuinely two-continuous-axis charts, so this crops the viewBox on both axes. Full design record
  in [ADR 0020](docs/adr/0020-scatter-bubble-2d-zoom-strategy.md); the short version:
  - Wheel zooms both axes uniformly (center-anchored, same factor per axis); dragging draws a
    brush rectangle and zooms into it on release (the standard convention for an unordered XY
    point cloud, versus Line/Area's ordered-series plain-drag-to-pan); Shift+drag pans instead,
    since drag itself is taken by brush-zoom; double-click resets.
  - Keyboard forks on whether point selection is wired: `ChartSelectionPrimitive.MoveActive` is
    modifier-blind and already owns plain arrows/Home/End, so when a chart is both zoomable and
    interactive, plain arrows keep navigating points exactly as before and zoom/pan requires a
    modifier (Shift+Arrow pans, Ctrl+Arrow/+/-/Home zooms/resets) — the one deliberate deviation
    from the Line/Area keyboard scheme, and the highest-value new test coverage this pass adds.
  - New Tier-1 `ChartRectZoomPrimitive` composes two existing `ChartZoomPrimitive` instances (one
    per axis) rather than introducing new zoom math; `ChartZoomPrimitive` itself gains one
    additive method, `SetVisible`, for jumping directly to an arbitrary window (what brush-zoom
    needs that none of `ZoomAt`/`PanBy`/`Reset` provide).
  - Both charts gain explicit `width`/`height`/`preserveAspectRatio="none"` SVG attributes
    (previously viewBox-only) — this pins the intrinsic aspect ratio so a two-axis-cropped
    viewBox never jitters, and means the rendered height is always derivable from the one
    existing width measurement, so no interop height measurement was needed.
  - One new interop method, `IChartZoomInterop.MeasureOffsetAsync` (the element's viewport
    offset) — brush-zoom, unlike pan, must convert an absolute drag rectangle into data-space,
    not just a pixel delta. Called once per gesture, never per pointermove.
  - Bubble radius stays computed from the full, unfiltered point list always — zoom affects only
    which bubbles are visible and where their centers land, never their size.

- **Forms: conditional fields, via `[DxField(DependsOn = ...)]`.** The roadmap's "Forms depth"
  item — a field can now gate on another field's live value, e.g. an escalation-notes field that
  only applies (and is only required) when a priority field is `High`. Scoped deliberately to
  conditional fields only: array and nested-object fields both need a real redesign of
  `IFormModel<TModel>`'s scalar-only `GetString`/`SetString` contract, which this pass didn't
  touch. Full design record in [ADR 0018](docs/adr/0018-conditional-form-fields.md); the short
  version:
  - Attribute-driven and generator-compiled, not a runtime delegate or an expression string —
    the same shape `Required`/`Min`/`Max`/`Pattern` already use, and the only shape consistent
    with ADR 0002's zero-reflection identity. A single hand-written evaluator,
    `FormFieldActivity.IsActive`, is called by `DxForm`'s renderer, `DxFormField`, the generated
    `Validate`, and `FormTool.ApplyArguments` alike — one implementation of "is this field
    currently active," not one per consumer.
  - One condition governs visibility *and* requiredness together (an inactive field is hidden,
    and its constraints don't apply) — a decoupled always-visible-but-conditionally-required
    model was considered and deliberately deferred, not overlooked.
  - Flat, single-field dependencies only, enforced at **compile time**: four new source-generator
    diagnostics, `DX2001`–`DX2004` (this repo's first — no existing generator reported any until
    now, a deliberately separate ID range from `BlazorDX.Analyzers`' `DX10xx` block), catch a
    `DependsOn` that doesn't name a real field, names a `Sensitive`/`[AiHidden]` field (an AI
    could never legally satisfy a condition on a field it's never told exists), chains to another
    conditional field, or references itself — each a compile error, not a silent gap.
  - The AI/MCP tool path is kept in sync, not deferred, per this project's "one model, two faces"
    identity. `FormTool.BuildInputSchema` emits a conditionally-required field via JSON Schema's
    `allOf`/`if`/`then` (draft-07+) — since the schema declares no `$schema` draft today, adding
    it is not a version bump, and any consumer that only reads `properties`/`required` (today's
    whole shape) just ignores the new keyword, a graceful degrade for hosts that don't evaluate
    conditionals. A plain-English clause is also appended to the field's `description` regardless
    of required/optional, since many function-calling hosts (OpenAI/Anthropic) don't guarantee
    they evaluate `allOf`/`if`/`then` at all — `description` is the more reliable signal in
    practice. `FormTool.ApplyArguments` is the actual enforcement boundary, independent of what
    the schema says: it now applies in two passes (every unconditional field first, then each
    conditional field re-checked against the *now-updated* target), silently skipping a
    conditionally-inactive field — the AI supplying a value for it in the same call has that
    value ignored, the same posture the existing `Sensitive` gate already uses.
  - Testing note: this repo has no Roslyn `CSharpGeneratorDriver`-based generator-test harness
    (existing "generator tests" compile a real annotated fixture in the same test project and
    inspect the output at runtime — they can't assert on a diagnostic that would fail that same
    project's build). Rather than build new Roslyn test infrastructure for this pass, DX2001–2004
    were verified once manually (a deliberately-bad model failing to compile with the expected
    message) rather than covered by an automated test — a scoped, stated limitation.

- **Forms: array and nested-object fields, via `List<T>` and `[DxFormModel]`-typed properties.**
  Closes out the "Forms depth" roadmap item in full — array and nested-object fields were
  deliberately deferred out of ADR 0018 because both need a real redesign of
  `IFormModel<TModel>`'s scalar-only `GetString`/`SetString` contract. Both landed together, one
  pass, not phased. Full design record in
  [ADR 0019](docs/adr/0019-array-and-nested-form-fields.md); the short version:
  - The redesign is additive: `IFormModel<TModel>` now extends a new non-generic
    `IFormModelUntyped` (get/set over `object` instead of `TModel`, plus nested/array accessors
    with default no-op bodies) rather than breaking its existing typed members. A scalar-only
    model's generated output is unaffected byte-for-byte outside two new thin `object`-overload
    wrapper methods every model now carries. `IFormModelUntyped` is the one genuinely new idea —
    it's what lets `DxForm`'s nested rendering and `FormTool`'s schema/argument-application
    recurse into a nested type's own independently-generated descriptor without being generic
    over that type; the generated code stays fully typed internally and only boxes at the
    interface boundary (an ordinary upcast, not reflection).
  - **No new attributes.** A `[DxField]` property whose own type carries `[DxFormModel]` becomes
    an Object field; one typed exactly `List<T>` (not `T[]`/`IList<T>`/etc. — the narrow shape
    supports "replace the whole collection," this pass's chosen mutation semantic) becomes an
    Array field, `T` either `[DxFormModel]`-tagged (array-of-nested) or a recognized scalar
    (array-of-scalar).
  - The generator's cross-type reference (`Outer`'s emitted code referencing `Address`'s own
    generated `AddressFormModel`) works without any pipeline-ordering dependency between the two
    types' independent generator invocations: `Outer`'s analysis reads `Address`'s declared-type
    symbol from the original syntax tree (unaffected by generation order) and emits a reference on
    faith in the generator's own deterministic naming convention; the C# compiler's final emit
    pass — which runs only after every generator invocation has contributed its output — is what
    actually resolves it.
  - Five new generator diagnostics, `DX2005`/`DX2007`–`DX2009` (per-type, alongside the existing
    DX2001–2004) plus `DX2006` (a new whole-compilation pass, since cycle detection is the one
    check that can't be answered from a single type's own field list): an array field's `List<T>`
    element must be `[DxFormModel]`-tagged or a recognized scalar (else some other collection
    shape, `T[]`/`IList<T>`/etc., is also caught here); a nesting/array reference cycle between
    `[DxFormModel]` types is a compile error (`FormTool`'s schema builder recurses over the
    field-kind graph unconditionally, so an uncaught cycle would recurse forever regardless of
    data); `DependsOn` can no longer cross a nested/array field boundary in either direction
    (`FormFieldActivity`'s evaluator has no dotted-path traversal, and this pass doesn't add one);
    and a referenced nested/array-element type must have at least one discovered field and an
    accessible public parameterless constructor (needed to materialize `new T()` for a null
    Object field or a new Array row — never reflection).
  - Rendering needed a new split, not just composition of existing pieces: `DxForm<TModel>` is
    now a thin typed wrapper around a new internal, **non-generic** `DxFormBody` (works over
    `IFormModelUntyped`/`object` — the actual field-rendering/validation engine). An Object field
    opens `DxFormBody` directly for its nested instance, inside the existing `DxFormSection`. Two
    real problems drove this, both caught during implementation, not by CI: first, the initial
    approach opened a nested `DxForm<TNested>` via `Type.MakeGenericType` — which CI's
    warnings-as-errors build rejected outright (`Type.MakeGenericType` is incompatible with
    Native AOT, and this repo publishes and smoke-tests an AOT build). Second, that same nested
    `DxForm` was rendering its own `<form>` element — invalid HTML nested inside the outer
    `<form>`. `DxFormBody` fixes both at once: it's a compile-time-known, non-generic type (so
    opening one is an ordinary component instantiation, never `MakeGenericType`), and it renders
    no wrapping element at all, so a nested `<form>` is structurally impossible, not just
    avoided. A currently-null nested property is materialized and attached before its sub-form
    renders, so there's no write-back step — the sub-form edits the real instance by reference
    identity. The outer form's own submit/`Refresh()` propagates into nested sub-forms by calling
    `.Refresh()` directly on captured `DxFormBody` references — no marker interface needed, since
    the concrete type is the same non-generic `DxFormBody` everywhere.
  - Array fields get a new Tier-1 primitive, `CollectionEditPrimitive<T>` (generic add/remove on
    top of the existing `ListReorder.Move<T>`/`RovingTabIndex` — reorder/drag/keyboard already
    proven by `SortablePrimitive`, which stays untouched rather than being force-generalized) and
    a new Tier-2 `DxFieldList<TItem>`, whose row chrome mirrors `DxSortableList`'s and whose
    remove button matches `DxChip`'s dismiss convention. Array-of-nested rows are the Object
    field's own rendering path, repeated per element — no third mechanism.
  - `FormTool.BuildInputSchema` recurses for `"object"`/`"array"` JSON-Schema types (with a
    defensive max-depth guard, since DX2006 already guarantees the real graph is acyclic).
    `ApplyArguments` gains a third pass, order-independent from the existing two (DX2007
    guarantees Object/Array fields never participate in `DependsOn`): Object fields
    materialize-then-recurse; Array fields **replace the whole collection on every call** — the
    simplest correct semantic, since a JSON payload carries no natural per-element identity to
    merge against.
  - **Stated v1 scope cuts:** array-of-scalar elements get no per-item constraint validation
    (only the list-level `Required` check); `List<T>` only, no `[DxField(Nested = true)]` escape
    hatch for a different collection shape; DX2006 is verified manually, the same documented
    testability gap ADR 0018 already accepted for DX2001–2004.

## [0.5.0] — 2026-09-02

### Removed

- **The Zero-Trust, Ephemeral AI Chat Conduit moved to its own repository, [AIEphemeral](https://github.com/logixrcorp/AIEphemeral).**
  `BlazorDX.Security.Rust`, `BlazorDX.Conduit`, `SecureEphemeralChat`/`EphemeralChatInterop`, the
  `/ai-chat` demo, and their test suites are gone from this repo — it was never general Blazor
  component-library infrastructure, and it can now version and release independently. This
  includes work that had only just landed in this same Unreleased section: `OnStateChanged` (a
  general-purpose `MountState` event stream), the `SimulateTamperRoute` dev-tool endpoint and its
  E2E coverage, the "Verifying the Ephemeral Chat Conduit" blog post, and the "Human Right to
  Forget" whitepaper — all ported over to AIEphemeral's own docs rather than lost. See that repo's
  `docs/adr/0001-zero-trust-ephemeral-chat-conduit.md` and `docs/whitepaper.md`.

### Added

- **`MarkdownRenderer`/`DxMarkdown`: GFM-lite table support.** The (now-moved) whitepaper's regulatory
  compliance-mapping tables were the first content in the repo to need Markdown tables, and the
  renderer had none — a `| a | b |` row rendered as a mangled run-on paragraph. Added a table
  parser (header row + `:---:`-style separator row + body rows) using the same encode-then-format
  security pattern as every other renderer path, plus `dx-markdown.css` table styling. Also fixed
  a real axe-core `scrollable-region-focusable` violation the same content surfaced: fenced code
  blocks are horizontally scrollable (`dx-markdown.css`'s `pre { overflow: auto }`) for long lines
  but had no way to reach that scroll via keyboard — added `tabindex="0"` to every rendered `<pre>`.

### Changed

- **Editorial system ("Architecture of Silence"): redesigned from a generic component-library
  look to a self-contained, bespoke magazine reading system.** The prior design reused the
  library's shared `dx-theme.css` tokens throughout (`--dx-accent`'s default `#2563eb`,
  `--dx-surface-alt`'s `#f8fafc`, `--dx-border`'s `#e2e8f0`) and Inter/Georgia for type — the same
  palette a Data Grid or a Dialog reads, which is exactly why the result read as generic SaaS
  chrome dressed up as "editorial" rather than something genuinely designed for long-form
  reading. Replaced with its own scoped token set (`--dx-ed-*`, defined on `.dx-editorial`) and a
  Fraunces (display) + Source Serif 4 (body) type pairing, loaded once in `App.razor`'s `<head>` —
  nothing else in the app references these font families, so this has zero effect outside
  Insights content. The color side of `--dx-ed-*` is deliberately NOT a competing palette: each
  token (`--dx-ed-paper`/`--dx-ed-ink`/`--dx-ed-rule`/`--dx-ed-accent`) is defined as `var(...)`
  over the corresponding shared `dx-theme.css` token (`--dx-surface`/`--dx-text`/`--dx-border`/
  `--dx-accent`) — an initial pass used a fully independent warm ink-on-paper/oxblood palette,
  which read as more distinctive in isolation but didn't match the rest of the site (the same
  blue/slate every nav bar, button, and other component uses); this indirection makes the
  editorial section look like part of the same product, and it also means dark mode now follows
  automatically through the same tokens rather than needing its own duplicate override block.
  The recurring "grey-bordered, rounded-corner, drop-shadowed card" used for the table of
  contents, technical sidebars, scrollytelling stages, and footer/related/index cards is gone,
  replaced with devices that actually read as print: a left-rule inset note for sidebars (echoing
  the pull-quote's own left rule), a rule-bounded numbered contents box for the TOC, sharp-edged
  panels for scrollytelling stages, and hairline-divider card grids for the footer/related/index
  listings. `.dx-editorial` itself now breaks out to full viewport width (the same
  `left:50%/-50vw` technique the hero and full-bleed figures already used), so the paper
  background runs edge-to-edge under the site's nav instead of sitting as a tinted rectangle
  inside the page shell's ~1000px cap.
  Two real, previously-uncaught accessibility bugs surfaced by this pass (found by adding the
  flagship article to the axe-core E2E sweep for the first time — see below): `.dx-avatar`'s
  default initials color (`--dx-accent` at full saturation over an 18%-tinted background)
  computed to ~4:1, just under WCAG AA's 4.5:1 for text — a pre-existing bug in the shared
  `DxAvatar` component, unrelated to this redesign specifically, just never exercised by an
  a11y-tested route before. Fixed by darkening the initials color toward `--dx-text` instead of
  further lightening an already-near-white background — this is a global fix, verified against
  the full 24-route axe-core sweep to confirm no other page regressed. Separately,
  `.dx-editorial-footnote-back`'s "↩" link relied on color alone to distinguish it from
  surrounding text (`link-in-text-block`) — fixed with an underline.
  Verified visually end to end (every section of the flagship article, the Insights hub/index
  grid) via real local browser rendering, not just "it compiles" — including one real layout bug
  the pass introduced and fixed before shipping: `.dx-insights-grid`/`.dx-editorial-related-grid`
  used `auto-fill`, which reserves empty grid tracks even with fewer cards than columns; once the
  container itself carried a background (for the hairline-divider look), an empty track rendered
  as a visible, ugly filled rectangle. Switched to `auto-fit`, which collapses empty tracks to
  zero width.
  Also added `/insights/articles/zero-trust-ephemeral-chat-conduit` to the axe-core accessibility
  E2E sweep (`AccessibilityE2ETests.cs`) — previously untested; this pass is what found both real
  violations above.

### Fixed

- **Production (`blazordx.com/ai-chat`), the actual final layer of the "could not be verified"
  saga: every real handshake still failed even after the DataProtection, `dx_security.wasm`, and
  `build-essential` fixes above all landed and were confirmed working.** Root-caused by adding
  temporary diagnostic logging across the full client-side handshake path (`ephemeral-chat.ts`,
  `SecureEphemeralChat.razor`, and a matching SHA-256-of-AES-key log on the server broker) and
  driving the live production site directly with browser automation until a real stack trace
  surfaced: `System.NotSupportedException: DeserializeNoConstructor` inside
  `AiChat.razor`'s `HandleEstablishAsync`, deserializing the broker's handshake response into
  `EphemeralHandshakeResult` — a positional record — via
  `JsonSerializer.Deserialize<EphemeralHandshakeResult>`'s reflection-based overload. Blazor
  WASM's default trimmer strips the constructor metadata that overload's reflection needs on a
  real `dotnet publish` (what every production image ships), but never on `dotnet build`/`dotnet
  run` (what every local dev loop uses) — which is exactly why this shipped untested against the
  one failure mode it actually hit, and why it took a real trimmed-publish reproduction, not just
  another local run, to catch. The exception was swallowed by `SecureEphemeralChat`'s outer catch
  and surfaced as an ordinary decrypt failure, indistinguishable from a real one without the
  exception text. Fixed by adding a source-generated `JsonSerializerContext`
  (`AiChatJsonContext`, following the existing `BlazorDX.Conduit/ConduitJson.cs` convention) and
  switching `HandleEstablishAsync` to the source-generated `Deserialize` overload — trim-safe by
  construction, no reflection involved. Verified for real: ran the exact `dotnet publish -c
  Release -p:UseAppHost=false` the Dockerfile uses, served the trimmed output locally, and drove a
  live saved-prompt handshake through a real browser — the assistant reply mounted and rendered
  "ASSISTANT · ENCRYPTED" with no exception, where the same steps against the unfixed trimmed
  build threw `DeserializeNoConstructor` every time. All temporary diagnostic logging added during
  the investigation (`session.rs`'s `debug_aes_key_sha256`, its `lib.rs` FFI export and
  `rust-loader.ts` binding, and the console/log instrumentation in `ephemeral-chat.ts`,
  `SecureEphemeralChat.razor`, and `DemoAiChatBroker.cs`) has been removed now that the root
  cause is fixed.

- **`docker build` failed the moment the previous fix tried to actually build `dx_security.wasm`:
  `error: linker \`cc\` not found`.** The Dockerfile's build stage never installed a C
  toolchain — `BlazorDX.Compute.Rust`'s dependency tree has no crates with a `build.rs`, so it
  never needed one. `BlazorDX.Security.Rust`'s crypto dependency chain
  (`aes-gcm`/`p256`/`sha2` → `generic-array`) does have one, and compiling *any* Rust build
  script — even for a `wasm32-unknown-unknown` target — links a small host-triple binary first,
  which needs a linker. GitHub Actions' `ubuntu-latest` runner ships `gcc` preinstalled, which is
  exactly why CI never caught this — only a `docker build` from the minimal
  `mcr.microsoft.com/dotnet/sdk:10.0` base image did. Added `build-essential` to the image's
  `apt-get install` line.

- **Production (`blazordx.com/ai-chat`): `dx_security.wasm` 404'd, so every assistant reply
  failed verification and rendered "This message could not be verified and was not shown."**
  Reported directly from the browser console on the live site. Root cause: the Dockerfile's
  explicit Rust build step (added specifically because the in-build MSBuild targets only
  *warn*, not fail, when cargo doesn't run — silently shipping an image missing the wasm the
  components import at runtime) only ever built `BlazorDX.Compute.Rust` → `dx_grid.wasm`. It
  predates `BlazorDX.Security.Rust` → `dx_security.wasm` — the ephemeral chat conduit's
  ECDH/AES-GCM crypto core, added later for the Zero-Trust Ephemeral Chat Conduit feature — and
  was never updated to build it too. The image build's own asset gate only checked for
  `dx_grid.wasm`, so a missing `dx_security.wasm` shipped silently instead of failing loudly,
  exactly the failure mode the gate exists to prevent.
  Fixed by building both wasm32 crates and gating on both artifacts in the publish output.
  Verified for real, not just "the file exists": ran the exact `cargo build`/`cp` sequence from
  the Dockerfile locally, confirmed `dx_security.wasm` serves 200 at the exact production path,
  then drove a real ephemeral-chat handshake end-to-end in a browser — the assistant reply now
  renders "ASSISTANT · ENCRYPTED" instead of the verification-failed state, reproducing and
  fixing the exact symptom from the live site.

- **Production (`blazordx.com`): every redeploy invalidated all outstanding sessions'
  antiforgery tokens, breaking in-flight form submissions with `AntiforgeryValidationException`.**
  Reported directly from the live site's container logs. Root cause: ASP.NET Core's
  DataProtection key ring was never configured to persist anywhere, so a fresh one is generated on
  every container start — any browser holding a cookie encrypted under the previous keys fails to
  decrypt it on its next request after a redeploy. Fixed in three parts: `Program.cs` now calls
  `AddDataProtection().SetApplicationName("BlazorDX.Demo").PersistKeysToFileSystem(...)` (the
  explicit application name matters too — without it, DataProtection derives its key-ring
  discriminator from the content root path, which can itself change between image builds and
  silently orphan a persisted ring); the `Dockerfile` points that path at `/keys` and declares it
  as a `VOLUME` with a comment explaining why; `deploy/docker-compose.yml` mounts an actual named
  volume there, since a bare `VOLUME` declaration alone still gets a fresh anonymous volume every
  time `docker compose up -d --build` recreates the container — which is exactly what the deploy
  README's own "Updating after a `git pull`" instructions do on every release.
  Verified locally: built and ran the Release server, confirmed the key file is written on first
  start, stopped and restarted it, confirmed the *same* key file (same GUID) is reused rather than
  a new one being generated — the actual failure mode this fixes.

- **WebKit-only ARIA violations in `DxBarChart`, `DxTreemap`, `DxSunburst`, and `DxNetworkGraph`,
  surfaced by CI's WebKit E2E job once the build gate (below) let it actually run.** Two related
  bugs, both from the same root pattern (interactive marks gaining `aria-label` with no explicit
  `role`) across every chart shape that supports per-mark selection:
  - `aria-prohibited-attr`: an SVG `<rect>`/`<circle>` mark with `aria-label` but no `role` has no
    ARIA name-permitting role under WebKit's implicit-role computation, so the label is rejected.
    Chromium/Firefox didn't flag it — only WebKit's stricter SVG-AAM mapping did. Fixed by adding
    `role="button"` to each interactive mark in `DxBarChart`, `DxTreemap`, `DxSunburst`, and
    `DxNetworkGraph` — the correct semantic (click/Enter/Space activates = selects the mark).
  - `nested-interactive`: fixing the above then exposed a second bug in `DxTreemap` — its SVG root
    kept `role="img"` unconditionally, even when its cells became `role="button"`, and ARIA
    forbids interactive descendants of an "img"-rolled element. `DxBarChart` already switched its
    root to `role="application"` when interactive; `DxTreemap`, `DxSunburst`, and `DxNetworkGraph`
    did not — all three now do the same, closing the same latent bug in `DxSunburst` before any
    demo happened to expose it.
  Verified for real against the actual WebKit engine (not just Chromium): full local `dotnet test`
  run against a live server with `BLAZORDX_BROWSER=webkit`, 23/23 axe-core checks passing including
  `/charts`. Also confirmed via CI's own WebKit job re-run that `FileManagerE2ETests`'s native-DnD
  failure in the same run was pre-existing WebKit/Playwright flakiness (passed on rerun, unrelated
  to any of this session's changes) rather than a regression — not fixed, just correctly ruled out.

- **Every route logged a console error: "An import map is added after module script load was
  triggered."** Fixing the CI build gate (below) let the E2E suite actually run for the first
  time in days, and it immediately caught this — the scrollytelling `<script type="module">` tag
  sat in `<head>` *above* `<ImportMap />` in `App.razor`. Per spec, the browser locks out further
  import maps once a module script starts loading, so any module script placed before `<ImportMap
  />` breaks it for the whole page, not just the one route that uses it. Moved the scrollytelling
  script tag below `<ImportMap />`. Verified live (console clean on `/keyboard`, `/powerbi`, and
  the Insights article that actually uses the script) and via the full local E2E suite (48/48
  passing, all three browsers covered by CI).

- **CI was red on every push since the "classic-meets-modern" editorial CSS pass (2026-07-18) —
  `dotnet build BlazorDX.slnx -c Release` failing on `NU1902`.** A fresh restore (CI always does
  one; a long-lived local checkout with cached `obj/` state doesn't re-audit) surfaced a moderate
  advisory on AngleSharp 1.1.2 (GHSA-pgww-w46g-26qg / CVE-2026-54570, an mXSS sanitizer-bypass),
  reached only transitively via bunit 1.31.3's test-time DOM parser — no shipping library or app
  code references AngleSharp. Every push in the interim built locally with `-p:NuGetAudit=false`
  as an undocumented local workaround; this closes that gap for real.
  Tried the obvious fix first — a central transitive pin to AngleSharp 1.5.0+ (the patched
  version) — and it's a binary break, not a drop-in: bunit 1.31.3's compiled
  `Bunit.RefreshableElementCollection` calls `AngleSharp.Dom.IHtmlCollection<T>.get_Item(int)` in
  a shape 1.5.x removed, so pinning past 1.5.0 threw `MissingMethodException` across 136 bUnit
  tests. bunit's own 2.x line depends on a patched AngleSharp, but upgrading a test framework two
  major versions across 1000+ tests is a separately-scoped, separately-regression-tested decision,
  not something to bundle into an unrelated fix.
  Suppressed the specific advisory instead, via `NuGetAuditSuppress` in `Directory.Build.props`,
  with the full reasoning recorded there — a targeted, standard NuGet mechanism for "this advisory
  doesn't apply to how we use this package," not a blanket audit bypass. Verified for real: clean
  `dotnet restore`/`dotnet build BlazorDX.slnx -c Release` with no bypass flag, then all 6 CI unit
  test steps run exactly as the workflow does (1172 tests total, 0 failures).

- **`.dx-chart-caption` failed WCAG AA color contrast on the demo shell's page background.**
  Adding `/charts` to the axe-core E2E accessibility sweep (see below) immediately caught a real
  violation: the shared caption style used `--dx-text-muted` (`#64748b`, Tailwind slate-500),
  correct against the component library's own white `--dx-surface` cards, but every chart on the
  `/charts` demo page renders directly on the demo shell's `body { background: #f1f5f9 }` — against
  that background `#64748b` only reaches ~4.34:1, just under the 4.5:1 AA threshold for normal
  text. Changed to `#475569` (slate-600), the same darker pairing the demo app's own CSS already
  uses everywhere else text sits on that `#f1f5f9` background (`.td-pri-low`, `.mail-label`,
  `.hr-leave-row`, etc.) — ~6.9:1 against the demo shell, ~7.6:1 against a plain white card, so the
  fix is safe in every context `.dx-chart-caption` renders in, not just this one page.

### Added

- **`/charts` now covered by the axe-core accessibility E2E sweep.** `AccessibilityE2ETests.cs`
  ran a real-browser axe-core check against ~20 routes (grid, calendar, scheduler, docs, etc.) but
  never the chart showcase — so the entire 25-chart-type family had zero automated real-browser
  accessibility verification, only bUnit's DOM-shape assertions. Added `/charts` to the route list.
  It immediately found the `color-contrast` violation fixed above — direct validation that the gap
  was real, not just theoretical. All 23 routes pass now, `/charts` included.

### Changed

- **`DxNetworkGraph`'s demo now shows its own selection feature.** `Charts.razor` rendered the
  network graph without wiring `OnNodeSelected`, unlike `DxBarChart` and `DxTreemap` right above
  it on the same page, which both demo their selection callback with live "Selected: X" feedback
  text. The component already supported click and Tab+Enter/Space node selection (each node is
  independently focusable, natural tab order rather than `ChartSelectionPrimitive`'s roving
  tabindex, since a 2D force layout doesn't reduce to one linear index — the same reasoning
  `DxTreemap` documents for its own cells) — it just wasn't visible to anyone browsing the demo.
  Now wired the same way as its siblings, with the same "Selected: X" caption pattern. Verified
  live: clicking a node updates the caption immediately, no console errors.

### Added

- **Three new `DxEditorial*` components (Phase 4, closing the reading-experience roadmap)**:
  `DxEditorialShareBar` (real share-intent links to X, LinkedIn, and email — deliberately no
  clipboard "copy link" button, since that needs JS interop this component family avoids; the
  family's one deliberate exception, `DxEditorialScrollytelling`'s reveal, is an explicit opt-in
  static asset, not per-component interop), `DxEditorialNewsletterSignup` (an inline email-capture
  form composing the library's own `DxTextBox` and `DxButton`; ships no backend of its own —
  `OnSubscribe` hands the host application a raw email string to do something real with), and
  `DxEditorialListen` (an "listen to this article" control wrapping a real narration file in a
  native `<audio controls>` element rather than a custom-styled player — BlazorDX ships no
  text-to-speech engine, and native controls are already fully accessible without JS interop).
  `DxEditorialShareBar` is wired into the flagship article using `NavigationManager.Uri` for a
  genuinely live URL (the same pattern `App.razor` already uses for its canonical/`og:url` tags).
  `DxEditorialNewsletterSignup` and `DxEditorialListen` ship tested and documented but
  deliberately unwired: there is no real newsletter service to hand a submitted email to, and no
  recorded narration asset exists for this piece — wiring either would mislead a reader who
  interacts with it, which is worse than the honest empty/unwired states this project already uses
  for content it doesn't have yet.
  6 new bUnit tests (1056 total passing, zero regressions). Documented in `docs/COMPONENTS.md`,
  `ComponentCatalog.cs`, and `blazordx-llms.md`.

- **Five new `DxEditorial*` components (Phase 3 of the reading-experience roadmap)**:
  `DxEditorialInsetFigure` (a small floated image with text wrapping via CSS `shape-outside` — a
  third image treatment alongside the full-bleed `DxEditorialFigure` and two-column
  `DxEditorialSpread`), `DxEditorialStatRow` (oversized numeric callouts, the data-journalism
  "big number" device), `DxEditorialFootnoteRef`/`DxEditorialFootnotes` (a superscript marker and
  its back-linked footnote list — the web analogue of a print footnote), and
  `DxEditorialGlossaryTerm` (an inline hover/focus definition composing the library's own
  `DxTooltip` rather than inventing a new interaction pattern).
  This pass also split `dx-editorial.css`: the 1000-line file cap (DX1000) finally caught up with
  three phases of additions, so the reading-experience/discovery rules (drop cap, table of
  contents, reading progress, author bio, tags, related, series nav, and everything in this
  entry) moved to a new `dx-editorial-extras.css` — load both stylesheets; `--measure` and the
  rest of the token set still resolve correctly across the file boundary since every selector in
  the new file is rendered as a descendant of `.dx-editorial`.
  Unlike `DxEditorialInsetFigure` (not wired into the flagship article — no spare image asset
  exists to demo it honestly; all 5 real photos are already placed), the other four ship with
  real usage: a footnote on "MCP" (linking to this repo's own `docs/ai-integration.md`, since MCP
  is a real term this project already uses, not a fabricated one), glossary terms on "ECDH" and
  "AES-256-GCM" in the crypto-handshake stage, and a 3-stat row (P-256, 256-bit AES key, 0
  plaintext copies stored) closing the piece. Verified live: the glossary tooltip renders the
  real definition text with `role="tooltip"` on focus, not just present in markup.
  6 new bUnit tests (1051 total passing, zero regressions). Documented in `docs/COMPONENTS.md`,
  `ComponentCatalog.cs`, and `blazordx-llms.md`.

- **Three new `DxEditorial*` components (Phase 2 of the reading-experience roadmap)**:
  `DxEditorialTagList` (topic pills, each a real `<a>` — not `DxChip`, which has no href),
  `DxEditorialRelated` (a "more like this" card row; renders nothing when `Entries` is empty,
  so it's always safe to include), and `DxEditorialSeriesNav` (previous/next navigation for a
  multi-part piece — the web analogue of a print jump line; either side may be omitted, and it
  renders nothing if both are).
  Unlike Phase 1, these aren't wired into the flagship article: `DxEditorialLayout` only has one
  real published piece to relate/tag/series-navigate, and per this project's standing "no seeded
  placeholder content" rule, faking a second piece or a topic archive just to demo the wiring
  would be dishonest. They ship with full test coverage (6 new bUnit tests, 1046 total passing)
  and curated usage examples in the docs catalog instead — the `/docs` pages for all three are
  honest that there's no live demo route yet, pointing to `/insights` rather than falsely
  implying they're rendered in the article.

- **Four new `DxEditorial*` components (Phase 1 of the reading-experience roadmap)**:
  `DxEditorialTableOfContents` (plain jump links to caller-supplied section IDs — the web
  descendant of a print magazine's contents page; no scrollspy in this version, a deliberate
  scope cut), `DxEditorialReadingProgress` (a fixed top bar filled via
  `animation-timeline: scroll(root)` — scroll-driven CSS, not a scroll-position listener, and
  since the fill is tied 1:1 to the reader's own scroll rather than auto-playing, it's exempt
  from `prefers-reduced-motion` the way the hero's Ken Burns zoom isn't), `DxEditorialDropCap`
  (a `::first-letter` wrapper — the oldest device in the magazine glossary, dating to scribes
  marking new sections as early as the 15th century), and `DxEditorialAuthorBio` (composes the
  library's own `DxAvatar`; `Initials` auto-derives from `Name` when omitted).
  Wired into the flagship article as real usage: a 7-entry table of contents linking to real
  `id`s added on the article's own section wrappers, the opening paragraph as a drop cap, and an
  author bio before the footer. 5 new bUnit tests (1040 total passing). Documented in
  `docs/COMPONENTS.md`, `ComponentCatalog.cs`, `blazordx-llms.md`, and three new checklist items
  under Editorial's existing pending-manual-pass accessibility section.
  This was scoped from a roadmap built by cross-referencing the current `DxEditorial*` family
  against both modern digital-editorial patterns and classic print-magazine anatomy (masthead,
  drop cap, kicker, deck, pull-quote, jump line, folio, callout, cutline, etc.) — see the roadmap
  for Phases 2–4 (tags/related-articles/series-nav, an inset `shape-outside` figure variant,
  stat rows, footnotes, a `DxTooltip`-based glossary term, share bar, newsletter block, audio
  narration control shell).

- **Promoted the Editorial family into the public library** as `DxEditorialLayout`,
  `DxEditorialFigure`, `DxEditorialSpread`, `DxEditorialPullQuote`, `DxEditorialSidebar`,
  `DxEditorialScrollytelling`/`DxEditorialScrollyStage`, `DxEditorialDissipation`, and
  `DxEditorialFooter` — previously demo-app-only `.razor` files, now hand-authored
  `RenderTreeBuilder` classes in `BlazorDX.Components` matching the rest of the library's
  zero-reflection convention (every other component but one is written this way; `.razor` is the
  exception, not the rule). Each has bUnit coverage, XML doc comments (surfaced on their new
  `/docs` pages via reflection, same as every other component), and an entry in the new
  "Editorial & long-form" category across `docs/COMPONENTS.md`, `ComponentCatalog.cs`, and
  `blazordx-llms.md`.
  `DxEditorialScrollytelling`'s reveal script moved from a co-located `.razor.js` (which required
  a matching `.razor` file) to a plain static asset, `dx-editorial-scrollytelling.js` — add it via
  one `<script type="module">` tag alongside the `dx-editorial.css` `<link>`, the same opt-in
  pattern as every other stylesheet in the library. A new `(scripting: none)` CSS guard keeps
  scrollytelling stages visible if scripting is genuinely disabled (the original had no such
  fallback); if scripting is enabled but the script tag is simply omitted, stages still won't
  reveal — the script is required, not optional, and is now documented as such everywhere the
  component is described. Added an "Editorial" section to the accessibility checklist
  (pending manual pass, like Excel/Word/HTMX docviewer already listed there).
  No consumer besides the demo app existed before this, so there's no breaking change — this is
  the family's first appearance in the installable package.

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

### Added

- **Localization & RTL, Phase 0.** The completion audit's largest remaining item: BlazorDX had
  zero localization infrastructure (no `IStringLocalizer`, no `.resx`) and 100% physical
  directional CSS. This phase proves the mechanism end to end on a real pilot slice rather than
  assuming one, and leaves the other ~130 components as a separate, later effort — see
  [ADR 0016](docs/adr/0016-localization-rtl-strategy.md) for the full decision record.
  `IStringLocalizer<T>` + root-level `.resx` was validated empirically against this repo's actual
  AOT-publish CI gate before committing to it (clean, zero trim/AOT warnings) rather than assumed
  safe. Piloted on `DxAlert` (a single-literal leaf case) and `DxDataGrid` (~20 literals; generic
  components need a non-generic marker type — `DxDataGridResources` — since
  `IStringLocalizer<DxDataGrid<TRow>>` would otherwise vary its resource name per closed `TRow`).
  A real resource-manifest-naming bug was caught and fixed during the pilot (root-level `.resx`
  instead of a `ResourcesPath` subfolder) before it could propagate across the other 130
  components — see the ADR for the root cause. New tests follow a sentinel + real-fallback pattern
  (`DxAlertTests.cs`, plus additions to `DxDataGridTests.cs`) that specifically catches this class
  of bug, since a naive "does the English string render" test can pass even when localization is
  silently broken. `dx-overlay.css` (backing `DxDialog`) converted from physical to logical CSS
  properties (`margin-inline-start`, `text-align: start`, etc.) as the RTL pilot — the standard
  browser-native fix that needs no `[dir="rtl"]` override for the common case — and the demo app
  gained a working `?dir=rtl` toggle (`App.razor`). A live in-browser culture-switch demo was
  attempted and deliberately not shipped; see the ADR's Consequences section for why, and what a
  future attempt should try instead.

### Fixed

- **Docker builds failed at the wasm-compile step** (`error: manifest path
  'src/BlazorDX.Security.Rust/Cargo.toml' does not exist`) since the Zero-Trust Ephemeral Chat
  Conduit's extraction to its own repo (AIEphemeral, see above): every reference to
  `BlazorDX.Security.Rust` in the C#/TS build chain was cleaned up at the time, but the root
  `Dockerfile` lives outside that file list and was missed. Dropped the dead `cargo build`/`cp`
  step and the `dx_security.wasm` publish-output gate; nothing downstream expects that file
  anymore.
- **CI silently ran on an unsupported Node version.** jsdom 30 (a `devDependency` of the TS
  interop bridge's test suite) requires Node `^22.22.2 || ^24.15.0 || >=26.0.0`; CI and the
  Dockerfile were still pinned to Node 20. This never surfaced as a build failure — `npm ci` only
  emits non-fatal `EBADENGINE` warnings for jsdom and three of its transitive deps rather than
  refusing to install — but it was never a supported combination. Bumped Node 20 → 22 across
  `ci.yml` (all 3 jobs), `release.yml`, and the Dockerfile's NodeSource setup script.

## [0.4.4] — 2026-06-28

### Added

- **Horizontal table cell merge** (merge right / split) in the Word editor.

## [0.4.3] — 2026-06-28

### Added

- **Table cell shading** in the Word editor.

## [0.4.2] — 2026-06-27

### Added

- **Paragraph line spacing + indentation** in the Word editor.

## [0.4.1] — 2026-06-27

### Added

- **Paragraph style dropdown** (Normal / Heading 1–3) in the Word editor.

## [0.4.0] — 2026-06-27

### Added

- **Word editor typography**: font family, font size, superscript/subscript.

## [0.3.19] — 2026-06-27

### Changed

- **Breaking: the Word editor now defaults to the model-driven core** ([ADR 0015](docs/adr/0015-model-driven-editing-core.md) Phase D) — `execCommand` parity is complete; the legacy `execCommand`-based path is no longer the default.

### Added

- **Insert images** into the Word editor (model-driven default + image insertion, flushing out the Phase D work).

## [0.3.18] — 2026-06-27

### Added

- **Word editor keyboard shortcuts**: Ctrl/Cmd+B/I/U/K, undo/redo.

## [0.3.17] — 2026-06-27

### Performance

- **2-D column virtualization** (column windowing) in the spreadsheet editor.

## [0.3.16] — 2026-06-27

### Performance

- **Incremental recalculation** in the spreadsheet editor, built on the 0.3.15 engine.

## [0.3.15] — 2026-06-27

### Added

- **Incremental recalc engine**: AST cache + dirty propagation for formula recalculation.

## [0.3.14] — 2026-06-27

### Added

- **Model-driven links** in the Word editor — [ADR 0015](docs/adr/0015-model-driven-editing-core.md) Phase D, completing `execCommand` parity for links.

## [0.3.13] — 2026-06-27

### Added

- **Model-driven lists** in the Word editor — [ADR 0015](docs/adr/0015-model-driven-editing-core.md) Phase D.

## [0.3.12] — 2026-06-27

### Added

- **Model-driven headings + color** in the Word editor — [ADR 0015](docs/adr/0015-model-driven-editing-core.md) Phase D parity.

## [0.3.11] — 2026-06-27

### Added

- **Model-driven alignment + clear-formatting** in the Word editor — [ADR 0015](docs/adr/0015-model-driven-editing-core.md) Phase D groundwork.

## [0.3.10] — 2026-06-27

### Added

- **Model-state undo/redo** in the Word editor, without a re-mount — [ADR 0015](docs/adr/0015-model-driven-editing-core.md) Phase C.

## [0.3.9] — 2026-06-27

### Added

- **Model-driven inline formatting** in the Word editor — [ADR 0015](docs/adr/0015-model-driven-editing-core.md) Phase B.

## [0.3.8] — 2026-06-27

### Added

- **Table editing UI** in the Word editor — the final of six documented feature gaps, all now closed.

### Fixed

- **E2E:** retried a transient Mono-WASM runtime-load flake in the smoke test.

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
