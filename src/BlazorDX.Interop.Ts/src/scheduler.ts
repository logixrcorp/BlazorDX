// Pointer-driven drag-to-move and drag-to-create for the scheduler time grid. The
// thin half of an otherwise all-C# component: every bit of layout, recurrence, and
// date math lives in .NET — this module only watches the pointer over the grid body,
// snaps the gesture to the day column and half-hour it lands on, auto-scrolls when the
// pointer nears a scroll edge, and reports one result back to .NET on release.
//
// Two gestures share one set of handlers:
//   1. Move — pointerdown on an event block carrying data-dx-key drags that event; on
//      release we report (sourceIndex, dayIndex, startHour) and .NET re-lays it out.
//   2. Create — pointerdown on empty grid sweeps out a range; on release we report
//      (dayIndex, startHour, endHour) and .NET raises its create callback.
//
// The grid is addressed by id so no ElementReference crosses the [JSImport] boundary
// (matching file-dnd.ts). Drag is purely an enhancement: keyboard active-cell nav and
// click-to-select work without this module, so a click (sub-threshold drag) is left
// untouched for Blazor's own onclick to handle.

type Cleanup = () => void;
type DragCallback = (resultJson: string) => void;

const registered = new Map<string, Cleanup>();

// Movement (px) the pointer must travel before a press becomes a drag rather than a click.
const DRAG_THRESHOLD = 4;
// Distance (px) from a scroll edge that starts auto-scroll, and the per-frame step.
const EDGE = 36;
const EDGE_SPEED = 12;

interface Geom {
  dayCount: number;
  startHour: number;
  endHour: number;
  hourHeight: number;
}

// Snap a fractional hour to the nearest half hour, clamped to the visible range.
function snapHour(hour: number, geom: Geom): number {
  const snapped = Math.round(hour * 2) / 2;
  return Math.min(geom.endHour, Math.max(geom.startHour, snapped));
}

// The day columns, left to right. Recomputed per gesture so it reflects the live layout.
function dayColumns(grid: HTMLElement): HTMLElement[] {
  return Array.from(grid.querySelectorAll<HTMLElement>(".dx-sched-col"));
}

// Index of the column whose horizontal extent contains clientX (clamped to the ends).
function columnAt(cols: HTMLElement[], clientX: number): number {
  for (let i = 0; i < cols.length; i++) {
    const rect = cols[i].getBoundingClientRect();
    if (clientX < rect.right) {
      return Math.max(0, i);
    }
  }
  return Math.max(0, cols.length - 1);
}

// Fractional hour-of-day a pointer Y maps to within a column (top of column == startHour).
function hourAt(col: HTMLElement, clientY: number, geom: Geom): number {
  const rect = col.getBoundingClientRect();
  return geom.startHour + (clientY - rect.top) / geom.hourHeight;
}

// Nearest scrollable ancestor (overflow auto/scroll, content taller than the box), so edge
// auto-scroll works whether the page or an inner pane is the scroll container.
function scrollParent(element: HTMLElement): HTMLElement {
  let node: HTMLElement | null = element.parentElement;
  while (node !== null) {
    const style = getComputedStyle(node);
    const overflowY = style.overflowY;
    if ((overflowY === "auto" || overflowY === "scroll") && node.scrollHeight > node.clientHeight) {
      return node;
    }
    node = node.parentElement;
  }
  return document.scrollingElement as HTMLElement ?? document.documentElement;
}

export function registerTimeGrid(
  gridId: string,
  dayCount: number,
  startHour: number,
  endHour: number,
  hourHeight: number,
  onDrag: DragCallback,
): void {
  const grid = document.getElementById(gridId);
  if (grid === null) {
    return;
  }
  unregister(gridId);

  const geom: Geom = { dayCount, startHour, endHour, hourHeight };

  // Per-gesture state, null between gestures.
  let mode: "move" | "create" | null = null;
  let started = false;            // crossed the drag threshold this gesture
  let downX = 0;
  let downY = 0;
  let sourceIndex = -1;           // move: the dragged event's data-dx-key
  let movingEl: HTMLElement | null = null;
  let grabOffsetPx = 0;           // move: pointer Y minus the event block's top
  let startCol: HTMLElement | null = null;   // create: column the sweep began in
  let preview: HTMLElement | null = null;    // create: the provisional selection box
  let scroller: HTMLElement | null = null;
  let edgeVelocity = 0;
  let rafId = 0;

  const onPointerDown = (event: PointerEvent) => {
    if (event.button !== 0 || mode !== null) {
      return;
    }
    const target = event.target as HTMLElement;
    const eventBlock = target.closest<HTMLElement>(".dx-sched-event[data-dx-key]");

    if (eventBlock !== null && grid.contains(eventBlock)) {
      mode = "move";
      movingEl = eventBlock;
      sourceIndex = Number.parseInt(eventBlock.dataset.dxKey ?? "-1", 10);
      grabOffsetPx = event.clientY - eventBlock.getBoundingClientRect().top;
    } else {
      const col = target.closest<HTMLElement>(".dx-sched-col");
      if (col === null || !grid.contains(col)) {
        return;
      }
      mode = "create";
      startCol = col;
    }

    downX = event.clientX;
    downY = event.clientY;
    scroller = scrollParent(grid);
    try {
      grid.setPointerCapture(event.pointerId);
    } catch {
      // Capture is best-effort: pointermove/up are also handled directly on the grid, so a
      // browser that refuses capture (e.g. no active pointer) still completes the gesture.
    }
  };

  const onPointerMove = (event: PointerEvent) => {
    if (mode === null) {
      return;
    }
    if (!started) {
      if (Math.abs(event.clientX - downX) < DRAG_THRESHOLD && Math.abs(event.clientY - downY) < DRAG_THRESHOLD) {
        return;   // still within click tolerance
      }
      started = true;
      if (mode === "move" && movingEl !== null) {
        movingEl.classList.add("dx-sched-event-dragging");
      }
    }

    if (mode === "move" && movingEl !== null) {
      movingEl.style.transform = `translate(${event.clientX - downX}px, ${event.clientY - downY}px)`;
    } else if (mode === "create" && startCol !== null) {
      drawCreatePreview(event.clientY);
    }

    updateAutoScroll(event.clientY);
    event.preventDefault();
  };

  const drawCreatePreview = (clientY: number) => {
    if (startCol === null) {
      return;
    }
    if (preview === null) {
      preview = document.createElement("div");
      preview.className = "dx-sched-create-preview";
      startCol.appendChild(preview);
    }
    const a = snapHour(hourAt(startCol, downY, geom), geom);
    const b = snapHour(hourAt(startCol, clientY, geom), geom);
    const top = (Math.min(a, b) - geom.startHour) * geom.hourHeight;
    const height = Math.max(geom.hourHeight / 2, Math.abs(b - a) * geom.hourHeight);
    preview.style.top = `${top}px`;
    preview.style.height = `${height}px`;
  };

  // Edge auto-scroll: when the pointer sits near a scroll edge, scroll a step each frame so
  // a drag can reach off-screen hours without the pointer leaving the grid.
  const updateAutoScroll = (clientY: number) => {
    if (scroller === null) {
      return;
    }
    const rect = scroller.getBoundingClientRect();
    if (clientY < rect.top + EDGE) {
      edgeVelocity = -EDGE_SPEED;
    } else if (clientY > rect.bottom - EDGE) {
      edgeVelocity = EDGE_SPEED;
    } else {
      edgeVelocity = 0;
    }
    if (edgeVelocity !== 0 && rafId === 0) {
      const tick = () => {
        if (edgeVelocity === 0) {
          rafId = 0;
          return;
        }
        scroller!.scrollTop += edgeVelocity;
        rafId = requestAnimationFrame(tick);
      };
      rafId = requestAnimationFrame(tick);
    }
  };

  const onPointerUp = (event: PointerEvent) => {
    if (mode === null) {
      return;
    }
    const cols = dayColumns(grid);
    const dayIndex = columnAt(cols, event.clientX);

    if (started && mode === "move") {
      // Re-derive the new top from the pointer minus where the block was grabbed.
      const col = cols[dayIndex] ?? startCol;
      const startHourValue = col !== null
        ? snapHour(geom.startHour + (event.clientY - grabOffsetPx - col.getBoundingClientRect().top) / geom.hourHeight, geom)
        : geom.startHour;
      report("move", sourceIndex, dayIndex, startHourValue, startHourValue);
      suppressNextClick();
    } else if (started && mode === "create" && startCol !== null) {
      const startDay = columnAt(cols, downX);
      const a = snapHour(hourAt(startCol, downY, geom), geom);
      const b = snapHour(hourAt(startCol, event.clientY, geom), geom);
      report("create", -1, startDay, a, b);
      suppressNextClick();
    }

    endGesture(event.pointerId);
  };

  const report = (kind: string, idx: number, day: number, startH: number, endH: number) => {
    onDrag(JSON.stringify({
      kind,
      sourceIndex: idx,
      dayIndex: day,
      startHour: startH,
      endHour: endH,
    }));
  };

  // Swallow the click that the browser fires after a drag so it does not also select.
  const suppressNextClick = () => {
    const swallow = (e: Event) => {
      e.stopPropagation();
      e.preventDefault();
      grid.removeEventListener("click", swallow, true);
    };
    grid.addEventListener("click", swallow, true);
    // If no click follows (some browsers), drop the listener on the next frame.
    requestAnimationFrame(() => grid.removeEventListener("click", swallow, true));
  };

  const endGesture = (pointerId: number) => {
    if (movingEl !== null) {
      movingEl.classList.remove("dx-sched-event-dragging");
      movingEl.style.transform = "";
    }
    preview?.remove();
    if (rafId !== 0) {
      cancelAnimationFrame(rafId);
    }
    try {
      grid.releasePointerCapture(pointerId);
    } catch {
      // Capture may already be gone (e.g. element re-rendered); nothing to release.
    }
    mode = null;
    started = false;
    sourceIndex = -1;
    movingEl = null;
    startCol = null;
    preview = null;
    scroller = null;
    edgeVelocity = 0;
    rafId = 0;
  };

  grid.addEventListener("pointerdown", onPointerDown);
  grid.addEventListener("pointermove", onPointerMove);
  grid.addEventListener("pointerup", onPointerUp);
  grid.addEventListener("pointercancel", onPointerUp);

  registered.set(gridId, () => {
    grid.removeEventListener("pointerdown", onPointerDown);
    grid.removeEventListener("pointermove", onPointerMove);
    grid.removeEventListener("pointerup", onPointerUp);
    grid.removeEventListener("pointercancel", onPointerUp);
    preview?.remove();
    if (rafId !== 0) {
      cancelAnimationFrame(rafId);
    }
  });
}

// Tears down whatever was wired for the grid id. Idempotent.
export function unregister(gridId: string): void {
  const cleanup = registered.get(gridId);
  if (cleanup !== undefined) {
    cleanup();
    registered.delete(gridId);
  }
}
