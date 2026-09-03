# ADR 0017 — Chart zoom/pan strategy (line & area)

**Status:** Accepted

## Context

The completion roadmap's one open "Chart interactivity" item was zoom/pan over the SVG charts —
point selection, hover, and legend toggling had all shipped. `ChartSelectionPrimitive`'s own doc
comment names the target explicitly: continuous/downsampled charts (line, area) are excluded from
point-selection because "those chart types are better served by a future zoom/pan interaction,
not per-point selection." This ADR is that decision record, scoped to exactly those two chart
types — not scatter, bar, or any other kind.

No zoom/pan/viewBox/wheel/drag code existed anywhere in the chart family before this; no shared
scale/axis primitive existed either (every chart, line chart included, inlines its own min/max
linear projection). This was greenfield work, not a retrofit of an existing mechanism.

## Decision

### viewBox-crop, not point reprojection

`DxLineChart`/`DxAreaChart` already set `preserveAspectRatio="none"` on their `<svg>` and
`vector-effect="non-scaling-stroke"` on their line stroke — unused before this feature (the
`viewBox` was static), but together they are exactly what a **viewBox crop** needs: independent
X/Y scaling and a constant on-screen stroke width regardless of scale. Zoom is implemented by
cropping the `viewBox`'s X extent to the visible domain window; point coordinates
(`BuildPoints()`/`BuildPaths()`) keep projecting against the **full** data domain, completely
unchanged from before this feature existed. At full zoom-out the dynamic `viewBox` reduces
exactly to the original static string — verified by a dedicated projection function
(`ViewBoxX`, distinct from the existing padded `ProjectX`) that maps the full domain onto the
*unpadded* `[0, Width]` canvas, so the reduction is exact, not approximate.

The alternative — recomputing every point's coordinates against the zoomed domain on each
gesture — was rejected: it would touch the one piece of existing math this feature could
otherwise leave completely alone, for no behavioral benefit over a crop the browser already does
for free via SVG's own coordinate scaling.

### Never re-downsamples past the initial LTTB pass

Both charts offload downsampling to `IGridCompute.DownsampleAsync` (Rust/wasm) once, when the
series changes. Zooming in re-projects the *already-downsampled* point set into a narrower
`viewBox`; it does not re-run LTTB against the full dataset for just the visible window. This
means zooming in past the initial downsample resolution reveals no new real data points — a
deliberate simplicity/robustness trade-off, not an oversight: re-downsampling on zoom would mean
an async Rust/wasm round-trip mid-gesture, with all the debounce/race-condition surface that
implies (rapid zoom/pan while a prior downsample request is still in flight), for a mechanism
that has no precedent anywhere else in this codebase. If finer in-zoom detail is ever needed, the
right fix is raising `Threshold` for a zoomable chart's initial render, not making zoom stateful
against an async backend.

### X-axis zoom only

Y always covers the full dataset's range regardless of X zoom level. Both target charts are
single-series, X-sequential data, and the standard "zoom a time series" model is horizontal —
narrow the X window, keep Y stable so magnitude stays visually comparable across zoom levels.
This also sidesteps `DxAreaChart`'s baseline-closing math needing to know about a filtered Y
range. Not silently precluded: a Y crop could be layered on top later (the `viewBox`'s Y extent
is untouched by this feature) without redoing `ChartZoomPrimitive`, which is itself geometry- and
axis-agnostic.

### Wheel-zoom is center-anchored, not cursor-anchored

Cursor-anchored zoom (keep the data value under the mouse fixed on screen) would need the SVG's
real rendered CSS-pixel width — `dx-chart.css` sets it to `width: 100%`, so the `Width` parameter
doesn't reflect the actual on-screen size once the page lays it out. Getting that measurement
requires an async JS interop call, and doing it on **every wheel tick** would be the first hot,
continuous-input path anywhere in this codebase to do an async round-trip per event — a real,
felt-in-the-browser lag risk during a rapid scroll gesture. Wheel-zoom instead zooms around the
current visible window's midpoint, reusing the exact same synchronous math as the keyboard `+`/
`-` handlers. No interop dependency at all on this path.

### Drag-pan does need one interop measurement — but only once per gesture

Converting a screen-pixel drag delta into a data-domain delta genuinely requires the SVG's real
rendered width (same responsive-width problem as above) — there's no way around it for a
*correct* pan speed (assuming the `Width` parameter as if it were the real width would make
panning feel too fast or too slow whenever CSS actually stretches the SVG to a different size,
which it does by design). `IChartZoomInterop.MeasureWidthAsync` is called once, at
`onpointerdown`, and the result is cached and reused synchronously for the rest of that drag
gesture — not per `onpointermove`. If the measurement comes back 0 (server/prerender via
`NullChartZoomInterop`, or the element isn't in the DOM yet), the pan gesture simply doesn't
start for that attempt — no division-by-zero, no garbage pan speed. Wheel-zoom and keyboard
zoom/pan never call this interop at all.

### Progressive enhancement: opt-in `Zoomable`, default `false`

The existing chart-family convention (`DxBarChart`, `DxScatterChart`, etc.) gates interactivity
on "has the host wired a callback" (`OnPointSelected.HasDelegate || OnPointHovered.HasDelegate`).
Zoom/pan has no equivalent natural signal — it's self-contained view state a chart can usefully
have with no host callback wired at all. But defaulting it on would silently hijack the page's
mouse-wheel scroll gesture on every existing line/area chart the instant this shipped. An
explicit `bool Zoomable` parameter (default `false`) is the correct equivalent of the existing
convention's actual intent — interactivity is consent-based, not ambient — even though the
mechanism differs from the delegate-presence check.

## Consequences

- A new Tier-1 primitive, `ChartZoomPrimitive` (`src/BlazorDX.Primitives/Charts/`), mirrors
  `ChartSelectionPrimitive`'s shape: pure C# state, no Blazor dependency, geometry-agnostic (real
  X units for `DxLineChart`, point-index units for `DxAreaChart`, which lays out by index).
- A new interop pair, `IChartZoomInterop`/`ChartZoomInterop`/`NullChartZoomInterop`
  (`src/BlazorDX.Interop/`) plus `chart-zoom.js`, registered in
  `InteropServiceCollectionExtensions`. Its only real capability is measuring an element's
  rendered CSS-pixel width — deliberately not named after grid/DOM infrastructure it doesn't
  otherwise touch, keeping the one-interop-per-concern convention this codebase already follows.
- **Known limitations, stated together rather than left implicit:** zoomed-in detail is capped at
  the initial LTTB downsample resolution (no new real data points appear from zooming); the Y
  axis never auto-fits to the zoomed X window; drag-pan's precision depends on a live DOM
  measurement taken once at gesture start, so it degrades to "the gesture doesn't start" rather
  than a wrong pan speed when that measurement is unavailable.
- `DxGraph.cs` needed explicit new plumbing for `Zoomable`/`OnZoomChanged` on its `Line`/`Area`
  kinds — it forwards parameters via individually-named calls per kind, not generic passthrough,
  so nothing here happened automatically.
- Scoped to `DxLineChart`/`DxAreaChart` only. Scatter, bar, and every other chart kind are
  unaffected and unchanged — this ADR does not claim a general charting zoom/pan story, only a
  continuous-domain one for the two kinds that were always the intended target.
