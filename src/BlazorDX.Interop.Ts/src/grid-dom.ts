// The thin DOM bridge for behaviors WebAssembly cannot perform itself. Elements
// are addressed by id (set by the C# component) so no ElementReference has to be
// marshalled across the [JSImport] boundary.

export interface ViewportMetrics {
  scrollTop: number;
  clientHeight: number;
  scrollHeight: number;
}

// Returns the current scroll window of the grid's scroll container as a flat
// number triple [scrollTop, clientHeight, scrollHeight]. A flat array marshals
// more cheaply than an object across the interop boundary.
export function measureViewport(elementId: string): number[] {
  const element = document.getElementById(elementId);
  if (element === null) {
    return [0, 0, 0];
  }
  return [element.scrollTop, element.clientHeight, element.scrollHeight];
}

// Full 2-D viewport metrics for a scroll container, as a flat number array:
// [scrollTop, scrollLeft, clientHeight, clientWidth, scrollHeight, scrollWidth]. The editable
// spreadsheet windows both rows and columns from a single scroll container, so it needs the
// horizontal axis too.
export function measureViewport2d(elementId: string): number[] {
  const element = document.getElementById(elementId);
  if (element === null) {
    return [0, 0, 0, 0, 0, 0];
  }
  return [
    element.scrollTop, element.scrollLeft,
    element.clientHeight, element.clientWidth,
    element.scrollHeight, element.scrollWidth,
  ];
}

// Subscribes the given .NET callback to the element's scroll events. Returns
// nothing; the component unsubscribes by disposing (the listener is GC'd with
// the element on navigation). Scroll is coalesced to one callback per frame via
// requestAnimationFrame, falling back to setTimeout when rAF is unavailable or
// suspended (e.g. a hidden/background tab, where rAF callbacks never fire).
export function subscribeScroll(elementId: string, onScroll: () => void): void {
  const element = document.getElementById(elementId);
  if (element === null) {
    return;
  }

  const schedule: (cb: () => void) => void =
    typeof requestAnimationFrame === "function" && !document.hidden
      ? requestAnimationFrame
      : (cb) => window.setTimeout(cb, 16);

  let queued = false;
  element.addEventListener("scroll", () => {
    if (queued) {
      return;
    }
    queued = true;
    schedule(() => {
      queued = false;
      onScroll();
    });
  });
}

// Triggers a client-side download of text content (e.g. an exported CSV) via an
// object URL, without round-tripping the bytes through the server. The URL is
// revoked after the click so the blob is not retained.
export function downloadText(filename: string, mime: string, content: string): void {
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
}

// Triggers a client-side download of binary content (e.g. an exported .xlsx
// workbook) from a base64 string — the bytes are marshalled as one string across
// the interop boundary, then decoded here into a typed array for the Blob.
export function downloadBytes(filename: string, mime: string, base64: string): void {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  const blob = new Blob([bytes], { type: mime });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
}

// Writes text to the system clipboard, returning whether it succeeded so the .NET
// side can report a failure (no permission, insecure context) rather than swallow it.
export function writeClipboard(text: string): Promise<boolean> {
  if (!navigator.clipboard) {
    return Promise.resolve(false);
  }
  return navigator.clipboard.writeText(text).then(
    () => true,
    () => false,
  );
}

// Scrolls a container to an absolute vertical position. Used by keyboard cell
// navigation to bring the active row into the virtualization window when it
// moves past the rendered edge.
export function scrollTo(elementId: string, top: number): void {
  const element = document.getElementById(elementId);
  if (element !== null) {
    element.scrollTop = top;
  }
}

// The keys keyboard cell navigation drives. Their default action (scrolling the
// container) is suppressed so navigation stays in control — but only outside text
// inputs, where these keys must keep working normally.
const NAV_KEYS = new Set<string>([
  "ArrowUp",
  "ArrowDown",
  "ArrowLeft",
  "ArrowRight",
  "PageUp",
  "PageDown",
  "Home",
  "End",
]);

// Suppresses native arrow/page scrolling for keydown events inside the grid, so
// keyboard cell navigation doesn't fight the browser. Text inputs (the column
// filters, the inline editor) are exempt — there the keys move the caret as
// usual. Blazor's own @onkeydown still fires (preventDefault stops the default
// action, not propagation), so navigation logic runs in C#.
export function suppressArrowKeys(elementId: string): void {
  const element = document.getElementById(elementId);
  if (element === null) {
    return;
  }
  element.addEventListener("keydown", (event: KeyboardEvent) => {
    const target = event.target as HTMLElement | null;
    const tag = target?.tagName;
    if (tag === "INPUT" || tag === "TEXTAREA" || target?.isContentEditable === true) {
      return;
    }
    if (NAV_KEYS.has(event.key)) {
      event.preventDefault();
    }
  });
}

// Moves focus to the first focusable descendant of an element. Used by the
// primitives layer (e.g. focus trapping a dialog or a grid's active cell).
export function focusFirst(elementId: string): void {
  const element = document.getElementById(elementId);
  if (element === null) {
    return;
  }
  const focusable = element.querySelector<HTMLElement>(
    'a[href], button:not([disabled]), input, select, textarea, [tabindex]:not([tabindex="-1"])',
  );
  (focusable ?? element).focus();
}
