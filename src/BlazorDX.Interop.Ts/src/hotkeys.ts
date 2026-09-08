// Global keyboard-shortcut bridge. One document-level keydown listener builds a
// normalized combo string (e.g. "ctrl+k", "ctrl+shift+p") and, only when that
// combo is in the registered set, calls preventDefault synchronously and notifies
// the .NET side. Matching must happen in JS so preventDefault can run before the
// browser acts on the key.

let bindings = new Set<string>();
let handler: ((combo: string) => void) | null = null;
let listening = false;

function comboOf(e: KeyboardEvent): string | null {
  const key = e.key.toLowerCase();
  if (key === "control" || key === "meta" || key === "alt" || key === "shift") {
    return null; // a modifier on its own is never a shortcut
  }

  const parts: string[] = [];
  if (e.ctrlKey || e.metaKey) parts.push("ctrl"); // treat Cmd as Ctrl
  if (e.altKey) parts.push("alt");
  if (e.shiftKey) parts.push("shift");
  parts.push(key);
  return parts.join("+");
}

// Same exemption grid-dom.ts already uses for its own arrow-key suppression: a global
// shortcut must not hijack a keystroke the user is actually typing into a text field. Without
// this, binding a plain letter (a common command-palette convention) would eat that character
// everywhere a text input, textarea, or contenteditable region has focus - including inside a
// DxDataGrid cell editor, a DxComboBox/DxCommandPalette filter, or a DxChat message box.
function isTextEntryTarget(target: EventTarget | null): boolean {
  const el = target as HTMLElement | null;
  const tag = el?.tagName;
  return tag === "INPUT" || tag === "TEXTAREA" || el?.isContentEditable === true;
}

export function subscribe(onMatch: (combo: string) => void): void {
  handler = onMatch;
  if (listening) {
    return;
  }
  listening = true;
  document.addEventListener("keydown", (e) => {
    if (isTextEntryTarget(e.target)) {
      return;
    }
    const combo = comboOf(e);
    if (combo !== null && bindings.has(combo) && handler !== null) {
      e.preventDefault();
      handler(combo);
    }
  });
}

export function setBindings(combos: string[]): void {
  bindings = new Set(combos);
}

export function unsubscribe(): void {
  handler = null;
  bindings = new Set();
}
