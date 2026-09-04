# ADR 0020 — Rectangular (both-axes) zoom/pan for DxScatterChart and DxBubbleChart

**Status:** Accepted

## Context

[ADR 0017](0017-chart-zoom-pan-strategy.md) shipped zoom/pan for `DxLineChart`/`DxAreaChart`, explicitly
scoped to those two — `docs/ROADMAP.md` names "other continuous-domain chart kinds
(scatter, etc.)" as deliberately deferred future work. `DxScatterChart` and
`DxBubbleChart` already exist and already have full point selection/hover via
`ChartSelectionPrimitive` (unaffected by this ADR); zoom/pan is the actual gap this
closes.

Line/Area are genuinely 1-D continuous: X varies, Y always covers the full dataset's
range (the standard "zoom a time series" model, so magnitude stays visually
comparable). Scatter/Bubble have no such convention — X and Y are both independent,
equally-real values. Asked X-only (mirror ADR 0017 exactly) vs. rectangular both-axes
zoom, the explicit choice was **both axes** — a genuinely bigger design ADR 0017 never
had to make, covered here.

## Decision

### Gesture set

- **Wheel → uniform zoom, both axes, center-anchored.** The same factor applied to
  each axis's own current-window midpoint. Not aspect-locked to the rendered W:H
  ratio — that would need a live width/height measurement on every wheel tick,
  reintroducing the hot-path-interop problem ADR 0017 rejected for cursor-anchoring.
- **Drag (no modifier) → brush-to-zoom.** Draw a rectangle, release to zoom into it —
  the standard convention for an unordered XY point cloud (Plotly/Highcharts/D3 brush
  all do this), versus Line/Area's ordered time-series, where plain-drag-to-pan is the
  more natural default.
- **Shift+Drag → 2-D pan.** Drag is taken by brush-zoom, so panning needs a distinct
  affordance — a modifier-held drag, reusing Line/Area's exact
  one-interop-call-per-gesture delta technique on both axes.
- **Double-click → reset.** Matches Line/Area.
- **Keyboard forks on whether point selection is wired** (`Interactive`), because
  `ChartSelectionPrimitive.MoveActive(string key, int count)` is modifier-blind (reads
  only the key, never `ShiftKey`/`CtrlKey`) and already owns plain
  arrows/Home/End unconditionally in both charts:
  - **Not interactive**: plain arrows pan (Left/Right → X, Up/Down → Y),
    `+`/`-`/Ctrl+ArrowUp/Down zoom, `Home`/`0` reset — the ADR 0017 scheme extended to
    two axes.
  - **Interactive**: plain arrows/Home/End stay **untouched**, still routed to
    `selection.MoveActive` — non-negotiable, since regressing existing keyboard point
    navigation is not acceptable. Zoom/pan requires a modifier instead: Shift+Arrow
    pans, Ctrl+ArrowUp/Down (or Ctrl+`+`/`-`) zooms, Ctrl+Home (or Ctrl+`0`) resets.
    The combined handler checks `ShiftKey`/`CtrlKey` *before* calling
    `selection.MoveActive`, so a modified arrow is never also treated as a
    (modifier-blind) selection move.
- **Explicitly deferred**, matching this repo's consistent narrow-v1-pass precedent
  (ADR 0017's X-only choice, ADR 0018's flat-DependsOn, ADR 0019's `List<T>`-only):
  cursor-anchored wheel zoom, touch/pinch gestures, brush-rectangle visual polish
  beyond a plain dashed `<rect>`, any `DxGraph` parameter beyond `Zoomable` and one new
  2-D callback.

### `ChartZoomPrimitive`/`ChartRectZoomPrimitive`

`ChartZoomPrimitive` gains one additive method, `SetVisible(min, max)` — the one
operation none of `ZoomAt`/`PanBy`/`Reset` provide (all relative to the current
window), needed because brush-to-zoom jumps to an arbitrary box rather than adjusting
the existing one. No existing method changed.

A new `ChartRectZoomPrimitive` composes two `ChartZoomPrimitive` instances as public
`X`/`Y` properties — callers keep reading axis state (`VisibleMin`, `DataMax`, ...)
through the same already-tested 1-D API Line/Area use. It adds only the coordination a
rectangular gesture needs: a combined `IsZoomed` (either axis), and operations that
apply to both axes together (`ZoomIn`/`ZoomOut`/`PanByFraction`/`ZoomToBox`/`Reset`).

Chart-component code is duplicated between `DxScatterChart` and `DxBubbleChart`, not
shared via a base class — matching the deliberate Line/Area precedent (no shared
projection helper exists anywhere in the chart family).

### Rendering: explicit SVG sizing, and the Y-axis viewBox inversion

Both charts previously set only `viewBox` (no `width`/`height`/`preserveAspectRatio`).
Add explicit `width`/`height` attributes and `preserveAspectRatio="none"`, mirroring
`DxLineChart`. This pins the intrinsic aspect ratio to the constant `Width`/`Height`
parameters — preventing visual jitter as a 2-axis-cropped viewBox changes shape on
every zoom step — and means the rendered pixel height is always derivable in C# as
`measuredWidth * (Height / (double)Width)`, so **no interop height measurement is
needed** even though brush/pan now operate on both axes.

`BuildPoints` is unchanged in spirit: it always projects against the full data domain
via `ProjectX`/`ProjectY` (the existing padded formula, now reading domain from
`zoom.X`/`zoom.Y`); only the SVG `viewBox` crops, via a parallel unpadded
`ViewBoxX`/`ViewBoxY`. Y-axis cropping is new — Line/Area never crop Y — and it has one
sharp edge: since `cy` is already computed top-down-flipped (`Height - Pad - ...`),
`ViewBoxY` must mirror that flip (`Height - fraction*Height`, not `fraction*Height`),
and the crop's `y0` comes from `ViewBoxY(VisibleMax)` (the larger data value is the
*smaller* screen coordinate — the top edge), not `VisibleMin`. Getting this backwards
silently shows the wrong half of the data with no visible error — covered by a direct
test asserting the exact `y0`/`h` produced by a known pan, not just "the numbers
changed."

Bubble radius (`Y2` → `MinRadius..MaxRadius`) is computed from the full, unfiltered
point list, unconditionally, exactly as before this ADR — only center position and
which bubbles fall inside the cropped view are affected by zoom. Covered by a direct
regression test asserting a bubble's `r` is bit-identical before/after zooming.

### `IChartZoomInterop.MeasureOffsetAsync` — one new method, purely additive

`MeasureWidthAsync` and every Line/Area call site are untouched. Pan only ever needs a
pixel *delta* (free from two `ClientX`/`ClientY` readings), which is why drag-pan alone
never needed an absolute position. Brush-to-zoom is different: it must convert an
absolute drag rectangle into absolute data-space bounds, which needs to know where the
element's origin sits in viewport coordinates. `MeasureOffsetAsync` returns
`(Left, Top)`, implemented via `[return: JSMarshalAs<JSType.Array<JSType.Number>>]`
over a `double[]` — not a new idiom in this codebase: `GridDomInterop.cs` already uses
the identical pattern for `MeasureViewport2dAsync`. Called once per gesture, at
pointerdown, alongside `MeasureWidthAsync` — never per pointermove, keeping the
"no hot-path interop" constraint intact.

### `FormTool`-style recursion doesn't apply here, but the parallel event-args type does

`DxGraph`'s existing `ChartZoomChangedEventArgs` is flat/1-D (`VisibleMin`/`VisibleMax`
only) — no room for an independent Y range. Rather than overload it, a new
`ChartZoomChanged2DEventArgs` (`XVisibleMin/Max`, `YVisibleMin/Max`, `IsZoomed`) and a
new `DxGraph.OnZoomChanged2D` parameter carry the 2-D payload; the existing `Zoomable`
bool parameter is reused as-is (works identically regardless of axis count).
`RenderScatter`/`RenderBubble` each gain the same two-line
`Zoomable`/`OnZoomChanged2D` forwarding `RenderLine`/`RenderArea` already do.

## Consequences

- New Tier-1 primitive `ChartRectZoomPrimitive` (`src/BlazorDX.Primitives/Charts/`) and
  one additive `ChartZoomPrimitive.SetVisible` method.
- One new interop method, `IChartZoomInterop.MeasureOffsetAsync`, implemented in
  `ChartZoomInterop`/`NullChartZoomInterop`/`chart-zoom.ts`.
- `DxScatterChart`/`DxBubbleChart` both gain explicit `width`/`height`/
  `preserveAspectRatio` SVG attributes, a dynamic (but numerically identical at full
  zoom-out) `viewBox`, and — only when `Zoomable` — a zoom-surface `<rect>`, a brush
  overlay, and a caption region with a reset button. `Zoomable=false` (the default)
  keeps the plot itself (points, dimensions, selection/hover) unaffected.
- New `ChartZoomChanged2DEventArgs` record and `DxGraph.OnZoomChanged2D` parameter.
- **Known limitations, stated together:** no cursor-anchored wheel zoom, no
  touch/pinch, no visual polish on the brush rectangle beyond a plain dashed outline;
  `DxGraph` forwards only `Zoomable`/`OnZoomChanged2D` for these two kinds, nothing
  else.
- Closes the "Chart interactivity" roadmap item's remaining "scatter, etc." line —
  every chart kind `ChartSelectionPrimitive`/`ChartZoomPrimitive`'s own doc comments
  ever named as an intended target now has the interactivity they described.
