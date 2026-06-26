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
