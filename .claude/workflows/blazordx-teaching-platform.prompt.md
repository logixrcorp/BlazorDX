# Mission: Make BlazorDX the canonical system self-taught developers use to learn Blazor

> Single-agent prompt companion to `blazordx-teaching-platform.js` (the multi-agent
> workflow variant). Use this to drive the same five workstreams as one session.
> Audience: self-taught developers (reference-style, browseable, low hand-holding).
> Mode: full autonomy (no check-in gates).

Working dir: `D:\Projects\BlazorDX` — a secure-by-default, zero-reflection, AOT-safe
.NET 10 Blazor system (C# + Rust→WASM + TypeScript + a static-SSR/HTMX tier). It is
already a near-reference implementation of modern Blazor fundamentals. Close the
remaining fundamentals gaps AND add a reference-style learning layer so a self-taught
developer can open the repo, browse any core Blazor concept, and see the concept, the
exact code that implements it, and *why* it's done that way — all in one place.

Fundamentals contract to teach against (from the three research docs):
Blazor (render modes, lifecycle, prerender/state persistence, sync context, binding,
EventCallback, state scoping, [JSImport] interop, forms/validation, routing/sections),
Rust (ownership/borrowing, FFI discipline, zero-cost abstractions, error handling,
Cargo-for-wasm), htmx (HATEOAS, LoB, triggers, targeting/swap, OOB, indicators, HX-*
headers, security/CSRF, progressive enhancement).

## Non-negotiable constraints (never violate — they are the project's reason to exist)
- Zero runtime reflection on hot/trimmable paths; binding/serialization stays
  source-generated. `dotnet publish -c Release` keeps **zero IL trim warnings**.
- Component-scoped state only — never a Singleton holding UI state (analyzer DX1002).
- No raw `MarkupString` unless sanitized via `BlazorDX.Security`.
- 1000-line file cap holds (DX1000 + FileLength MSBuild target).
- Everything builds (`dotnet build BlazorDX.slnx`), `dotnet test` is green, new browser
  behavior gets a Playwright E2E test, Release publish stays trim-clean. Verify in a
  real browser — don't assume. Match surrounding style. One ADR per architectural choice.

## Workstream 1 — Prerender state persistence (closes the audited Blazor gap)
No `PersistentComponentState`/`[PersistentState]` exists today, so data-fetching
components double-execute on hydration. Add declarative `[PersistentState]` persistence
(via a reusable primitive, not per-component boilerplate) so the fetch runs once.
Acceptance: a demo page with a visible fetch counter proving single execution; an E2E
test asserting no duplicate fetch on hydration.

## Workstream 2 — Demonstrate ALL render modes (only WASM is shown today)
Add demo pages under Static SSR, InteractiveServer, InteractiveWebAssembly, and
InteractiveAuto, each labeled with its mode and trade-offs, plus a lifecycle "x-ray"
page that logs SetParametersAsync → OnInitialized(Async) → OnParametersSet(Async) →
OnAfterRender(Async) in order, shows `firstRender`, and visibly demonstrates the
prerender double-execution and Workstream 1's fix. Acceptance: a "Render Modes &
Lifecycle" nav section, one page per mode, E2E smoke test per mode.

## Workstream 3 — Bring the HTMX tier up to fundamentals (the thinnest area)
Without breaking its HATEOAS/LoB correctness, extend `DxHtmxForm`/helpers with:
HX-Request detection (HTML partial to htmx, full page to direct navigation); antiforgery
re-enabled and handled for htmx POSTs (replace the demo's `.DisableAntiforgery()`); the
other verbs (get/put/patch/delete); `hx-trigger` modifiers (delay/throttle/changed/
revealed/poll); `hx-swap-oob` multi-target updates; `htmx-indicator` + double-submit
prevention; one response-header demo (HX-Trigger or HX-Retarget). Acceptance: a debounced
active-search demo, an OOB multi-update demo, and an inline-validation demo returning
errors in an HTML fragment; the no-JS full-page fallback works; E2E for swap round-trips
and the no-JS path.

## Workstream 4 — Rust polish (low risk)
Bump `BlazorDX.Compute.Rust` to edition 2024 / Rust 1.85+, fix lints (keep the
`unsafe extern "C"` FFI boundary clean under stricter `unsafe_op_in_unsafe_fn`), confirm
crate tests pass and the managed C# twin stays semantically identical.

## Workstream 5 — Reference-style learning layer (for self-taught developers)
Optimize for someone browsing on their own, not a taught course:
- **`docs/learn/` reference set:** one self-contained "Concept → Code → Why" page per
  fundamental, each deep-linking to the exact `file:line` that implements it and to the
  live demo page. Skimmable, searchable, cross-linked — not sequential lessons.
- **In-app source viewer:** every demo page shows its own source beside the running
  result (reuse existing doc/static-asset plumbing; no reflection).
- **A concept index / map:** `docs/learn/README.md` is a lookup table (fundamental →
  doc → demo → source), plus a "by tier" and "by difficulty" view so people can self-route.
- **Divergences as the sharpest lessons:** explicitly show the textbook default beside
  BlazorDX's version and explain the trade — source-generated forms vs
  `EditForm`+`DataAnnotationsValidator`, and `[JSImport]` vs `IJSRuntime` — so a reader
  learns the rule and the reason to break it.

## Definition of done
- All five workstreams complete; solution builds, `dotnet test` green, E2E green across
  Chromium/Firefox/WebKit, zero trim warnings on Release publish.
- For every core fundamental, `docs/learn/README.md` resolves it to a labeled demo page
  and the source line that implements it; each demo shows its own source in-app.
- README.md, docs/ARCHITECTURE.md, docs/ROADMAP.md, COMPONENTS.md updated to reflect the
  learning layer and closed gaps; one ADR per architectural decision.

Full autonomy: execute all five workstreams end to end in vertical slices (implement →
demo → test → learn-doc → verify in a real browser), only surfacing genuine blockers.
