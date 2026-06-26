// Thin bridge for the WYSIWYG editor. Formatting uses document.execCommand —
// deprecated but still the only built-in way to apply rich formatting to a
// contentEditable region, and universally supported in current browsers. The
// .NET side reads the live HTML back out and routes it through an injected
// sanitizer, so this module never has to trust the markup it returns.

export function exec(command: string, value: string): void {
  document.execCommand(command, false, value === "" ? undefined : value);
}

// Prompts for a URL and links the current selection. Only http/https/mailto are
// applied; anything else (javascript:, data:, relative) is rejected so a hostile URL
// never reaches the document. An empty prompt cancels.
export function createLink(): void {
  const input = window.prompt("Link URL (http, https, or mailto):", "https://");
  if (input === null) {
    return;
  }

  const url = input.trim();
  if (/^(https?:|mailto:)/i.test(url)) {
    document.execCommand("createLink", false, url);
  }
}

// The last selection range observed inside a rich-text surface. A color <input> steals
// the contentEditable selection when clicked, so we remember it and restore it before
// applying the color (otherwise execCommand has nothing to format).
let lastEditorRange: Range | null = null;

document.addEventListener("selectionchange", () => {
  const sel = window.getSelection();
  if (!sel || sel.rangeCount === 0) {
    return;
  }

  const range = sel.getRangeAt(0);
  let node: Node | null = range.commonAncestorContainer;
  while (node) {
    if (node instanceof HTMLElement && node.classList.contains("dx-rte-surface")) {
      lastEditorRange = range.cloneRange();
      return;
    }

    node = node.parentNode;
  }
});

// Applies a foreground (foreColor) or highlight (hiliteColor) color to the remembered
// selection. Uses styleWithCSS so the output is a parseable `style="color:…"`.
export function applyColor(command: string, color: string): void {
  if (lastEditorRange) {
    const sel = window.getSelection();
    if (sel) {
      sel.removeAllRanges();
      sel.addRange(lastEditorRange);
    }
  }

  document.execCommand("styleWithCSS", false, "true");
  document.execCommand(command, false, color);
  document.execCommand("styleWithCSS", false, "false");
}

export function getHtml(elementId: string): string {
  const el = document.getElementById(elementId);
  return el ? el.innerHTML : "";
}

export function setHtml(elementId: string, html: string): void {
  const el = document.getElementById(elementId);
  if (el) {
    el.innerHTML = html;
  }
}

export function focusEditor(elementId: string): void {
  document.getElementById(elementId)?.focus();
}

// Selects the next/previous occurrence of `query` in the editor (wrapping at the ends) and
// scrolls it into view, relative to the current caret. Returns the 1-based index of the
// selected match, or 0 if there are none. Search runs over the live text nodes, so it owns
// the selection without any model<->DOM coordinate mapping.
export function findInEditor(
  elementId: string, query: string, forward: boolean, caseSensitive: boolean): number {
  const root = document.getElementById(elementId);
  if (!root || !query) {
    return 0;
  }

  // Flatten the text nodes, remembering where each starts in the combined string.
  const segments: { node: Text; start: number }[] = [];
  let text = "";
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  let node: Node | null;
  while ((node = walker.nextNode())) {
    const t = node as Text;
    segments.push({ node: t, start: text.length });
    text += t.data;
  }

  const hay = caseSensitive ? text : text.toLowerCase();
  const needle = caseSensitive ? query : query.toLowerCase();
  const matches: number[] = [];
  for (let i = hay.indexOf(needle); i >= 0; i = hay.indexOf(needle, i + needle.length)) {
    matches.push(i);
  }

  if (matches.length === 0) {
    return 0;
  }

  // Where is the caret now (combined-string offset)? -1 / end when there's no selection.
  let caret = forward ? -1 : text.length;
  const sel = window.getSelection();
  if (sel && sel.rangeCount > 0) {
    const r = sel.getRangeAt(0);
    for (const seg of segments) {
      if (seg.node === r.startContainer) {
        caret = seg.start + r.startOffset;
        break;
      }
    }
  }

  let idx: number;
  if (forward) {
    idx = matches.findIndex((m) => m > caret);
    if (idx < 0) { idx = 0; } // wrap to first
  } else {
    idx = -1;
    for (let k = matches.length - 1; k >= 0; k--) {
      if (matches[k] < caret) { idx = k; break; }
    }
    if (idx < 0) { idx = matches.length - 1; } // wrap to last
  }

  const range = document.createRange();
  point(range, segments, matches[idx], true);
  point(range, segments, matches[idx] + needle.length, false);
  if (sel) {
    sel.removeAllRanges();
    sel.addRange(range);
  }

  (range.startContainer.parentElement ?? root).scrollIntoView({ block: "nearest" });
  return idx + 1;
}

// Places a range edge at a combined-string offset by locating the owning text node.
function point(range: Range, segments: { node: Text; start: number }[], offset: number, isStart: boolean): void {
  for (const seg of segments) {
    if (offset <= seg.start + seg.node.length) {
      const local = Math.max(0, offset - seg.start);
      if (isStart) { range.setStart(seg.node, local); } else { range.setEnd(seg.node, local); }
      return;
    }
  }
}
