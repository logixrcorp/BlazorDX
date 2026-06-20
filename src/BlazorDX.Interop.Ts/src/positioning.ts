// Anchored positioning for floating elements (Popover, Tooltip, Dropdown, ...).
// Positions a floating element relative to an anchor with collision handling:
// it flips to the opposite side when the preferred side lacks room, and shifts
// along the cross axis to stay within the viewport. Both elements are addressed
// by id. Dismissal (Escape / click-outside) is handled separately by overlay.ts.

type Side = "top" | "bottom" | "left" | "right";
type Align = "start" | "center" | "end";

const VIEWPORT_PADDING = 8;
const OPPOSITE: Record<Side, Side> = {
  top: "bottom",
  bottom: "top",
  left: "right",
  right: "left",
};

const trackers = new Map<string, () => void>();

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(value, max));
}

function chooseSide(anchor: DOMRect, floating: DOMRect, side: Side, offset: number): Side {
  const room = {
    top: anchor.top,
    bottom: window.innerHeight - anchor.bottom,
    left: anchor.left,
    right: window.innerWidth - anchor.right,
  };
  const needed = side === "top" || side === "bottom"
    ? floating.height + offset
    : floating.width + offset;
  // Flip only if the preferred side is too tight and the opposite has more room.
  if (room[side] < needed && room[OPPOSITE[side]] > room[side]) {
    return OPPOSITE[side];
  }
  return side;
}

function place(
  anchor: HTMLElement,
  floating: HTMLElement,
  preferred: Side,
  align: Align,
  offset: number,
): void {
  const a = anchor.getBoundingClientRect();
  const f = floating.getBoundingClientRect();
  const side = chooseSide(a, f, preferred, offset);

  let top = 0;
  let left = 0;
  const vertical = side === "top" || side === "bottom";

  if (side === "bottom") top = a.bottom + offset;
  else if (side === "top") top = a.top - f.height - offset;
  else if (side === "right") left = a.right + offset;
  else left = a.left - f.width - offset;

  // Cross-axis alignment.
  if (vertical) {
    if (align === "start") left = a.left;
    else if (align === "end") left = a.right - f.width;
    else left = a.left + (a.width - f.width) / 2;
  } else {
    if (align === "start") top = a.top;
    else if (align === "end") top = a.bottom - f.height;
    else top = a.top + (a.height - f.height) / 2;
  }

  // Shift to keep the element on screen.
  left = clamp(left, VIEWPORT_PADDING, window.innerWidth - f.width - VIEWPORT_PADDING);
  top = clamp(top, VIEWPORT_PADDING, window.innerHeight - f.height - VIEWPORT_PADDING);

  floating.style.position = "fixed";
  floating.style.left = `${Math.round(left)}px`;
  floating.style.top = `${Math.round(top)}px`;
  floating.dataset.dxSide = side; // exposes the resolved side for arrow/styling
}

export function attach(
  floatingId: string,
  anchorId: string,
  side: Side,
  align: Align,
  offset: number,
): void {
  const floating = document.getElementById(floatingId);
  const anchor = document.getElementById(anchorId);
  if (floating === null || anchor === null) {
    return;
  }

  detach(floatingId);
  const update = () => place(anchor, floating, side, align, offset);
  update();

  // Reposition while scrolling (capture: catch scrolls in any container) and on resize.
  window.addEventListener("scroll", update, true);
  window.addEventListener("resize", update);
  trackers.set(floatingId, () => {
    window.removeEventListener("scroll", update, true);
    window.removeEventListener("resize", update);
  });
}

export function detach(floatingId: string): void {
  const stop = trackers.get(floatingId);
  if (stop !== undefined) {
    stop();
    trackers.delete(floatingId);
  }
}
