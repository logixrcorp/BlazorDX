// Document-viewer bridge. The browser's native PDF viewer (an <embed>/<iframe>)
// exposes no scriptable print API, so "print" routes through the standard
// window.print() dialog. When the embedded document lives in a same-origin
// <iframe> we print that frame's own content window (so only the document prints,
// not the surrounding app); otherwise — a cross-origin frame, or a plain <embed>
// whose content window is not reachable — we fall back to printing the host window.

export function print(frameId: string): void {
  const el = frameId ? document.getElementById(frameId) : null;

  // Only an <iframe> reliably exposes a same-origin contentWindow to print.
  if (el instanceof HTMLIFrameElement) {
    try {
      const win = el.contentWindow;
      if (win) {
        win.focus();
        win.print();
        return;
      }
    } catch {
      // Cross-origin frame: fall through to printing the host window.
    }
  }

  window.print();
}
