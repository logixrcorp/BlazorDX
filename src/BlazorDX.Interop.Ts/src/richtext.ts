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

// Prompts for a URL and returns it (http/https/mailto only) without touching the DOM, so the
// model-driven core can set the link on its own model. Returns "" on cancel or an unsafe scheme.
export function promptLink(): string {
  const input = window.prompt("Link URL (http, https, or mailto):", "https://");
  if (input === null) {
    return "";
  }

  const url = input.trim();
  return /^(https?:|mailto:)/i.test(url) ? url : "";
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

// Wires Ctrl/Cmd keyboard shortcuts on the editor surface to .NET, mapping each to a command
// string: B/I/U -> bold/italic/underline, K -> createLink, Z -> undo (Shift+Z or Y -> redo).
// preventDefault() is selective — only for handled shortcuts — so normal typing is untouched
// and the browser's own contentEditable bold/undo (which would bypass a model-driven editor)
// never fires. The handler is GC'd with the element on navigation.
export function subscribeShortcuts(elementId: string, onShortcut: (command: string) => void): void {
  const el = document.getElementById(elementId);
  if (el === null) {
    return;
  }

  el.addEventListener("keydown", (e: KeyboardEvent) => {
    if (!(e.ctrlKey || e.metaKey) || e.altKey) {
      return;
    }

    let command = "";
    switch (e.key.toLowerCase()) {
      case "b": command = "bold"; break;
      case "i": command = "italic"; break;
      case "u": command = "underline"; break;
      case "k": command = "createLink"; break;
      case "z": command = e.shiftKey ? "redo" : "undo"; break;
      case "y": command = "redo"; break;
      default: return;
    }

    e.preventDefault();
    onShortcut(command);
  });
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

// Opens a native file picker for an image and resolves with "mimeType|base64", or "" if the
// user cancels or the file isn't a supported image. The .NET side turns this into a WordImage
// and inserts it into the document model.
export function pickImage(): Promise<string> {
  return new Promise((resolve) => {
    const input = document.createElement("input");
    input.type = "file";
    input.accept = "image/png,image/jpeg,image/gif,image/webp";
    input.style.display = "none";
    document.body.appendChild(input);

    let settled = false;
    const finish = (value: string) => {
      if (settled) {
        return;
      }

      settled = true;
      input.remove();
      resolve(value);
    };

    input.addEventListener("change", () => {
      const file = input.files && input.files[0];
      if (!file) {
        finish("");
        return;
      }

      const reader = new FileReader();
      reader.onload = () => {
        const match = /^data:([^;]+);base64,(.*)$/.exec(String(reader.result ?? ""));
        finish(match ? `${match[1]}|${match[2]}` : "");
      };
      reader.onerror = () => finish("");
      reader.readAsDataURL(file);
    });

    // There is no cancel event for a file dialog; when focus returns without a selection, resolve
    // empty after a tick so the awaiting .NET task never hangs.
    window.addEventListener("focus", () => setTimeout(() => finish(""), 600), { once: true });
    input.click();
  });
}

// Reports the caret's position within a table as "tableIndex,rowIndex,colIndex" (all
// 0-based, the table index among the editor's tables in document order), or "" when the
// caret is not inside a table. Lets table edits target the right cell in the model.
export function getTableCell(elementId: string): string {
  const root = document.getElementById(elementId);
  if (!root) {
    return "";
  }

  const sel = window.getSelection();
  if (!sel || sel.rangeCount === 0) {
    return "";
  }

  const start = sel.getRangeAt(0).startContainer;
  const from = start.nodeType === Node.ELEMENT_NODE ? (start as Element) : start.parentElement;
  const cell = from?.closest("td,th");
  const table = cell?.closest("table");
  if (!cell || !table || !root.contains(table)) {
    return "";
  }

  const tableIndex = Array.from(root.querySelectorAll("table")).indexOf(table);
  const row = cell.closest("tr");
  const rowIndex = row ? Array.from(table.querySelectorAll("tr")).indexOf(row) : -1;
  const colIndex = row
    ? Array.from(row.children).filter((c) => c.tagName === "TD" || c.tagName === "TH").indexOf(cell)
    : -1;
  return tableIndex < 0 || rowIndex < 0 || colIndex < 0 ? "" : `${tableIndex},${rowIndex},${colIndex}`;
}

// The run-containers the model addresses, in document order. Each holds a single run
// sequence: a heading/paragraph, a list item, or a table cell. This selector's
// document-order match is identical to the model's run-container enumeration, so an
// index into one indexes the other — no data attributes needed.
const RUN_CONTAINER_SELECTOR = "h1,h2,h3,h4,h5,h6,p,li,td,th";

// Reports the current selection as "containerIndex,start,end": the run-container (in
// document order) the selection sits in, and the character offsets within that container's
// text (start <= end). Returns "" when there is no selection, when it spans more than one
// container, or when it lies outside the editor. This is the owned selection the
// model-driven editing core maps its commands onto (ADR-0015).
export function getSelectionRange(elementId: string): string {
  const root = document.getElementById(elementId);
  if (!root) {
    return "";
  }

  const sel = window.getSelection();
  if (!sel || sel.rangeCount === 0) {
    return "";
  }

  const container = containerOf(sel.anchorNode, root);
  if (!container || container !== containerOf(sel.focusNode, root)) {
    return ""; // no container, or a cross-container selection we can't address yet
  }

  const index = Array.from(root.querySelectorAll(RUN_CONTAINER_SELECTOR)).indexOf(container);
  if (index < 0) {
    return "";
  }

  const a = offsetWithin(container, sel.anchorNode!, sel.anchorOffset);
  const f = offsetWithin(container, sel.focusNode!, sel.focusOffset);
  return `${index},${Math.min(a, f)},${Math.max(a, f)}`;
}

// Restores a selection addressed as a run-container index plus character offsets (the
// inverse of getSelectionRange), and focuses the editor so editing can continue.
export function setSelectionRange(
  elementId: string, containerIndex: number, start: number, end: number): void {
  const root = document.getElementById(elementId);
  if (!root) {
    return;
  }

  const container = Array.from(root.querySelectorAll(RUN_CONTAINER_SELECTOR))[containerIndex];
  if (!container) {
    return;
  }

  const range = document.createRange();
  locate(container, start, range, true);
  locate(container, end, range, false);
  const sel = window.getSelection();
  if (sel) {
    sel.removeAllRanges();
    sel.addRange(range);
  }

  root.focus();
}

// The nearest run-container of a node, or null when the node is outside the editor.
function containerOf(node: Node | null, root: HTMLElement): Element | null {
  const el = node && (node.nodeType === Node.ELEMENT_NODE ? (node as Element) : node.parentElement);
  const container = el?.closest(RUN_CONTAINER_SELECTOR);
  return container && root.contains(container) ? container : null;
}

// Character offset from the start of a container to (node, nodeOffset), counting text only.
// Uses a Range so element- and text-node anchors are handled uniformly.
function offsetWithin(container: Element, node: Node, nodeOffset: number): number {
  const r = document.createRange();
  r.selectNodeContents(container);
  try {
    r.setEnd(node, nodeOffset);
  } catch {
    return container.textContent?.length ?? 0;
  }

  return r.toString().length;
}

// Places a range edge at a character offset within a single container by walking its text
// nodes. Past-the-end offsets clamp to the container end.
function locate(container: Element, offset: number, range: Range, isStart: boolean): void {
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
  let acc = 0;
  let node: Node | null;
  while ((node = walker.nextNode())) {
    const t = node as Text;
    if (offset <= acc + t.length) {
      const local = Math.max(0, offset - acc);
      if (isStart) { range.setStart(t, local); } else { range.setEnd(t, local); }
      return;
    }

    acc += t.length;
  }

  const tail = container.childNodes.length;
  if (isStart) { range.setStart(container, tail); } else { range.setEnd(container, tail); }
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
