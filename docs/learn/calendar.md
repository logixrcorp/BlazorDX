# Calendar — Concept → Code → Why

**Demo:** `/calendar` · **Source:** [`DxCalendar.cs`](../../src/BlazorDX.Components/DxCalendar.cs),
[`CalendarPrimitive.cs`](../../src/BlazorDX.Primitives/Inputs/CalendarPrimitive.cs)

## Concept

An always-visible month calendar for picking dates inline — distinct from
[`DxDatePicker`](../../src/BlazorDX.Components/DxDatePicker.cs), which keeps its grid inside a popup,
and from [`DxScheduler`](scheduler.md), which is event-centric. It selects a single date or a
start/end range (with a hover preview while the range is half-picked), respects `Min`/`Max` and an
arbitrary `IsDateDisabled` predicate, decorates a set of `MarkedDates` with a dot, and accepts a
per-day `DayTemplate`. The week starts on the culture's first day.

## Code

- **Tier split (ADR 0001).** Date math, view/selection state, and the active-cell model live in the
  headless [`CalendarPrimitive.cs`](../../src/BlazorDX.Primitives/Inputs/CalendarPrimitive.cs);
  DOM + styling live in [`DxCalendar.cs`](../../src/BlazorDX.Components/DxCalendar.cs).
- **Selection** is in `SelectAsync` ([`CalendarPrimitive.cs`](../../src/BlazorDX.Primitives/Inputs/CalendarPrimitive.cs)):
  single mode raises `ValueChanged`; range mode sets the start on the first click and closes the
  range — ordered start ≤ end — on the second, raising `OnRangeSelected`. A click after a complete
  range starts a fresh one. `IsInRange` also reflects the in-progress hover (`SetHover`).
- **The grid** is a real ARIA `grid` → `row` → `gridcell` (so axe's `aria-required-children` is
  satisfied) with `aria-activedescendant` on the focused day and `OnGridKeyDownAsync` handling
  arrows / Home / End / PageUp/Down (Shift = year) / Enter / Space.
- **Culture-aware week start.** `WeekdayHeaders()` and `GridStart()` both rotate by
  `Culture.DateTimeFormat.FirstDayOfWeek`, so the grid starts on the locale's first weekday rather
  than a hard-coded Sunday.

## Why (accessibility + non-negotiables)

- **One keyboard model, ARIA grid (ADR 0012).** The month is a single tab stop; the focused day is
  virtual (`aria-activedescendant`), matching the DataGrid/scheduler pattern. Days carry
  `aria-selected`, `aria-disabled`, and `aria-current="date"`; a disabled day wires no click handler,
  so it is unreachable by mouse or keyboard rather than merely styled.
- **Status, not colour alone.** Selection/range/marked states each get a class (and the marker is a
  glyph), so they survive high-contrast and colour-blind viewing; selected/range fills use white text
  on the accent for AA contrast.
- **Verified by** [`DxCalendarTests.cs`](../../tests/BlazorDX.Components.Tests/DxCalendarTests.cs)
  (grid shape, single/range selection, ordering, bounds, marks, keyboard) and the axe gate on
  `/calendar` in [`AccessibilityE2ETests.cs`](../../tests/BlazorDX.E2E.Tests/AccessibilityE2ETests.cs).
