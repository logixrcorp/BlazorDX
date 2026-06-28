# Scheduler — Concept → Code → Why

**Demo:** `/scheduler` · **Source:** [`DxScheduler.cs`](../../src/BlazorDX.Components/DxScheduler.cs),
[`SchedulerPrimitive.cs`](../../src/BlazorDX.Primitives/Scheduling/SchedulerPrimitive.cs)

## Concept

A calendar scheduler with Week / Month / Day views. Week and Day render a time grid
with absolutely positioned event blocks; Month renders a date grid with per-day
event buttons. All views are keyboard-navigable in 2-D and announce view/date
changes. Recurring events (RRULE-style) are expanded in C#, and the time grid
supports pointer drag-to-move / drag-to-create through a thin TypeScript bridge with
edge auto-scroll — both layered on without disturbing the keyboard model.

## Code

- **Tier split (ADR 0001).** Date math, view state, the active-cell model and the
  overlap-lane layout live in the headless
  [`SchedulerPrimitive.cs:58`](../../src/BlazorDX.Primitives/Scheduling/SchedulerPrimitive.cs).
  DOM + styling live in [`DxScheduler.cs:24`](../../src/BlazorDX.Components/DxScheduler.cs).
- **View switch** is a WAI-ARIA tablist with roving tabindex + arrow keys:
  [`DxScheduler.cs:88`](../../src/BlazorDX.Components/DxScheduler.cs) (`BuildViewSwitch`)
  and [`:120`](../../src/BlazorDX.Components/DxScheduler.cs) (`OnViewSwitchKeyDownAsync`).
- **2-D keyboard navigation** (arrows / Home / End / PageUp / PageDown) is one model
  shared by both grids: [`SchedulerPrimitive.cs:336`](../../src/BlazorDX.Primitives/Scheduling/SchedulerPrimitive.cs)
  (`MoveActiveCell`), dispatched from
  [`DxScheduler.cs:447`](../../src/BlazorDX.Components/DxScheduler.cs) (`OnGridKeyDownAsync`).
- **Overlap layout** is computed in pure C# today (the Rust lane kernel is deferred);
  events are positioned in [`DxScheduler.cs:271`](../../src/BlazorDX.Components/DxScheduler.cs)
  (`BuildEvent`).
- **Recurrence** is expanded for the visible window only (bounded, no unbounded
  series) in [`SchedulerPrimitive.cs`](../../src/BlazorDX.Primitives/Scheduling/SchedulerPrimitive.cs)
  (`ExpandOccurrences` / `OccurrenceStarts`); `Count` is measured from the seed so paging
  the view never shifts the dates a rule produces.
- **Drag-to-move / drag-to-create** is a thin pointer bridge
  ([`scheduler.ts`](../../src/BlazorDX.Interop.Ts/src/scheduler.ts) via
  [`ISchedulerInterop`](../../src/BlazorDX.Interop/ISchedulerInterop.cs)): JS only snaps the
  gesture to the day/half-hour and auto-scrolls at the edges, then reports one result; the
  move/create math and validation stay in C# (`ApplyMoveAsync` / `ApplyCreateAsync`). Only
  concrete events are draggable — recurrence occurrences carry no row key.

## Why (accessibility + non-negotiables)

- **ARIA role choice is deliberate, per view.** The Week/Day time grid uses
  `role="application"` because absolutely positioned event blocks do not map cleanly
  to `grid > row > gridcell`; the rationale is documented inline at
  [`DxScheduler.cs:200`](../../src/BlazorDX.Components/DxScheduler.cs). The Month view
  *does* fit, so it uses a real `role="grid"` with `role="row"`/`role="gridcell"` and
  `aria-colcount`/`aria-rowcount` — [`DxScheduler.cs:329`](../../src/BlazorDX.Components/DxScheduler.cs)
  and the per-week row wrapper at [`:354`](../../src/BlazorDX.Components/DxScheduler.cs).
  Both keep axe clean while preserving one keyboard model (ADR 0012).
- **1.4.1 — never colour alone.** Event category is rendered as icon **+ text**, not
  colour: [`DxScheduler.cs:287`](../../src/BlazorDX.Components/DxScheduler.cs) (time grid)
  and [`:430`](../../src/BlazorDX.Components/DxScheduler.cs) (month).
- **1.4.3 contrast.** Consumer-supplied event colours are darkened via `color-mix`
  so white label text clears AA for any colour:
  [`DxScheduler.cs:464`](../../src/BlazorDX.Components/DxScheduler.cs) (`EventBackground`).
- **4.1.3 announcements.** A polite `role=status` region announces the current view
  and range: [`DxScheduler.cs:151`](../../src/BlazorDX.Components/DxScheduler.cs).
- **Render mode (ADR 0013): InteractiveWebAssembly.** The scheduler is fine-grained
  and compute-heavy (per-keystroke navigation, overlap layout) — a server round-trip
  per interaction would regress it. ADR 0013 lists the scheduler explicitly under
  the interactive-WASM tier; ADR 0011 governs the future Rust layout kernel's profile.

### Verified by

bUnit unit tests cover view switching, multi-day/midnight events, the keyboard
navigation model, and the month grid's ARIA dimensions; axe checks the route
([`AccessibilityE2ETests.cs`](../../tests/BlazorDX.E2E.Tests/AccessibilityE2ETests.cs)).
