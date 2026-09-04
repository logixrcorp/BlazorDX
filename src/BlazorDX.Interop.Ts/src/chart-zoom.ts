// The one DOM measurement a zoomable chart (line, area) needs: its SVG's actual rendered
// CSS-pixel width. dx-chart.css sets the SVG to `width: 100%`, so the C#-side `Width` parameter
// (used only for the internal viewBox/point-projection math) does not reflect the real on-screen
// size once the page lays it out — converting a drag-pan pixel delta into a data-domain delta
// needs the real width. Measured once per pan gesture (at pointerdown), not per pointermove.

export function measureWidth(elementId: string): number {
  const element = document.getElementById(elementId);
  return element === null ? 0 : element.clientWidth;
}

// The element's rendered top-left corner in viewport CSS pixels — needed only by a
// rectangular brush-to-zoom gesture (scatter/bubble), which must convert an absolute
// drag rectangle into data-space, unlike pan (line/area, and scatter/bubble's own
// shift-drag-pan) which only ever needs a pixel delta between two ClientX/ClientY
// readings. Called once per gesture, at pointerdown, alongside measureWidth.
export function measureOffset(elementId: string): number[] {
  const element = document.getElementById(elementId);
  if (element === null) {
    return [0, 0];
  }
  const rect = element.getBoundingClientRect();
  return [rect.left, rect.top];
}
