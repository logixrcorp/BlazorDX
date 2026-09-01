# ADR 0016 — Localization mechanism and RTL layout strategy

**Status:** Accepted

## Context

The completion audit named this the single largest remaining engineering item: zero
localization infrastructure existed anywhere in the repo (no `IStringLocalizer`, no
`.resx`, no reference to `Microsoft.Extensions.Localization`), and ~90 user-facing strings
sat as hardcoded literals across the 133 `Dx*` components in `src/BlazorDX.Components`
(zero in `src/BlazorDX.Primitives` — primitives are unlabeled by design, per
[ADR 0001](0001-two-tier-headless.md)'s two-tier split, so this work is scoped to the
styled tier only). Separately, CSS across `wwwroot/*.css` was 100% physical directional
properties, 0% logical.

This decision covers Phase 0 only: proving a string-externalization and RTL mechanism
end to end on a real pilot slice, verified against this repo's actual AOT-publish CI gate
rather than assumed. The other ~130 components are a separate, later, multi-phase effort.

## The fork: `IStringLocalizer<T>` vs. a source-generated alternative

BlazorDX's identity is built on zero runtime reflection ([ADR 0002](0002-zero-reflection-source-generation.md):
*"Forbid runtime reflection on any hot or trimmable path... A custom generator is a
maintenance surface, accepted for the safety it gives"*), enforced by a dedicated CI job
(`aot-publish`) that AOT-publishes the whole demo with `-p:EnableAot=true` and treats
warnings as errors. `IStringLocalizer<T>`'s default implementation resolves through
`ResourceManager`/satellite assemblies, which has a documented history of AOT/trim
friction in Blazor WASM. Rather than assume either way, this was resolved empirically: a
real component was wired to `IStringLocalizer<T>` + `.resx`, then published with the
*exact* command `aot-publish` runs in CI
(`dotnet publish samples/BlazorDX.Demo/BlazorDX.Demo/BlazorDX.Demo.csproj -c Release
-p:EnableAot=true -o publish`).

**Result: clean.** Zero warnings, zero errors, on a build that treats every trim/AOT
warning as an error. The published output's WASM client bundle
(`wwwroot/_framework/fr/BlazorDX.Components.resources.*.wasm`) genuinely contains the
French satellite resource assembly, trimmed and AOT-compiled alongside everything else.

## Decision

**`IStringLocalizer<T>` + `.resx`, at the project root — not under a `ResourcesPath`
subfolder.**

The first attempt put `.resx` files under a `Localization/` subfolder with
`<ResourcesPath>Localization</ResourcesPath>` set on the csproj. This compiled and ran,
and appeared to work for the pilot component (`DxAlert`) — but only by coincidence: its
one resource key, `"Dismiss"`, is identical to its English value, so a broken lookup
silently falling back to returning the raw *key* was indistinguishable from a correct
lookup returning the real *value*. It surfaced for real on the second pilot component
(`DxDataGrid`), whose keys (`"SelectAllRows"`) don't match their values (`"Select all
rows"`) — every localized string came back as the literal key.

Root cause: MSBuild embeds a `.resx` under `ResourcesPath` with that path folded into the
manifest resource name (`BlazorDX.Components.Localization.DxAlert.resources`), but
`IStringLocalizerFactory`'s default resource-name computation is unaware of
`ResourcesPath` unless it is *also* configured identically on the runtime side —
`AddLocalization(o => o.ResourcesPath = "Localization")` — in **every** consuming app and
**every** test project. That's an easy, silent way to reintroduce this exact bug across
130 more components and dozens of test files. Root-level `.resx` (`DxAlert.resx`, next to
`DxAlert.cs`) matches the default manifest-name convention (`BlazorDX.Components.DxAlert`)
with zero extra configuration anywhere, eliminating the whole bug class.

**Verification technique for future pilots:** a bUnit test asserting a component renders
its real English string proves nothing — a broken lookup falling back to the raw key can
coincidentally match. Assert a *sentinel* value from an explicitly-registered fake
localizer (proves the component is wired to the DI-resolved localizer, not still
hardcoded), **and** assert the real English value with the real `AddLocalization()`
factory registered and no override (proves the `.resx` resource pipeline itself
resolves correctly) — both are required; either alone can pass while the other is broken.

**Generic components need a non-generic marker type.** `DxDataGrid<TRow>` cannot inject
`IStringLocalizer<DxDataGrid<TRow>>` directly: the resource-name computation for an open
generic type varies by which `TRow` closes it, so `DxDataGrid<Person>` and
`DxDataGrid<Order>` would each expect a differently-named resource even though one
`DxDataGridResources.resx` file is meant to serve every `TRow`. The fix is the standard
one: a non-generic marker type (`DxDataGridResources`) that `IStringLocalizer<T>` injects
against instead, with the resx file named to match the marker.

**RTL: convert physical CSS properties to logical properties**
(`margin-inline-start`/`inset-inline-start`/`text-align: start` etc.), which the browser
flips automatically under `dir="rtl"` with no separate override rule needed for the common
case. Piloted on `dx-overlay.css` (backs `DxDialog`): 5 physical declarations converted
(`text-align: left` → `start` ×3, `margin-left` → `margin-inline-start` ×2), zero
physical directional declarations remain in the file.

**Not every `left`/`right` is a text-direction concern.** `DxSheet`'s
`dx-sheet-panel-left`/`-right` classes are an explicit "which physical screen edge" API
choice a developer makes (`Side="right"`), the same way every other drawer/sheet component
works — not something that should flip under `dir="rtl"`, or `Side="right"` would visually
dock left, contradicting its own name. Left physical, documented in place.

## Consequences

- **A real, reproducible resource-naming bug was caught and fixed before it could
  propagate across the other 130 components' worth of future work** — the actual value of
  running this as an empirical Phase 0 spike rather than assuming a pattern from the first
  component that happened to work.
- Every future pilot's test suite must include both the sentinel test and the real-resource
  fallback test (see above) — a convention, not enforced by tooling yet. `DX1003` (a future
  analyzer banning new hardcoded literal strings, timed for after Phase 0, before the
  multi-component rollout) does not by itself guard against this specific resource-naming
  regression; that needs the two-test pattern.
- `.resx` files live at each component's project root, one pair per marker type
  (`Component.resx` + `Component.{culture}.resx`), not organized into a subfolder — a
  minor organizational cost accepted for eliminating an entire class of silent
  misconfiguration.
- Generic components need a marker type for localization; this is now the established
  pattern for any other generic `Dx*` component the later rollout touches.
- **A live browser demonstration of culture-switching was attempted and deliberately
  abandoned, not shipped as broken code.** A `?culture=` query-string toggle was wired on
  both the server (via `RequestLocalizationMiddleware`, confirmed correctly setting
  `CultureInfo.CurrentCulture` at the request level via a diagnostic endpoint) and the
  client (via direct `CultureInfo.CurrentCulture`/`CurrentUICulture` assignment before
  `RunAsync()`). Neither reliably changed what `@rendermode InteractiveWebAssembly`
  components actually rendered: the server-side value never reached
  `DxAlert.BuildRenderTree()`'s `IStringLocalizer` call (Razor Components' prerender
  dispatch for WASM render mode appears not to flow ambient request culture into render
  execution — plausibly deliberate, since real client-side WASM execution has no HTTP
  request to derive a culture from at all), and WASM hydration adopting an
  already-prerendered DOM does not force a fresh render of unchanged components, so even a
  correctly-applied client-side culture switch wouldn't visibly repaint. This is a real,
  unresolved gap in *demonstrating* culture-switching live in a browser — it is not a gap
  in the underlying mechanism, which is proven independently and more rigorously by the
  bUnit sentinel/fallback tests and the real AOT publish. Future work wanting a live demo
  toggle should investigate `IHostEnvironmentAuthenticationStateProvider`-style
  state-flow patterns or a forced `StateHasChanged()` after culture changes, rather than
  assuming ambient `CultureInfo` propagates through Blazor's renderer the way it does
  through plain ASP.NET Core middleware.
