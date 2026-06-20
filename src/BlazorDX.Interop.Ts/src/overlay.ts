// The DOM behaviors shared by every overlay (Dialog, Sheet, Popover, ...):
// focus trapping, scroll locking, and dismissal (Escape + click-outside). Each
// overlay is addressed by element id; opening registers a set of listeners and
// returns nothing, and closing tears them down. The .NET side owns the open/close
// lifecycle and supplies a dismiss callback.

type Cleanup = () => void;

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

const openOverlays = new Map<string, Cleanup>();

function focusableWithin(root: HTMLElement): HTMLElement[] {
  return Array.from(root.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(
    (el) => el.offsetParent !== null,
  );
}

// Keeps Tab/Shift+Tab cycling inside the overlay.
function makeTabTrap(root: HTMLElement): (event: KeyboardEvent) => void {
  return (event: KeyboardEvent) => {
    if (event.key !== "Tab") {
      return;
    }
    const focusable = focusableWithin(root);
    if (focusable.length === 0) {
      event.preventDefault();
      return;
    }
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement as HTMLElement | null;
    if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  };
}

export function open(
  elementId: string,
  ignoreId: string,
  trapFocus: boolean,
  lockScroll: boolean,
  closeOnEsc: boolean,
  closeOnOutsideClick: boolean,
  onDismiss: () => void,
): void {
  const element = document.getElementById(elementId);
  if (element === null) {
    return;
  }

  close(elementId); // never double-register
  const cleanups: Cleanup[] = [];

  if (lockScroll) {
    const previous = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    cleanups.push(() => {
      document.body.style.overflow = previous;
    });
  }

  if (closeOnEsc) {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onDismiss();
      }
    };
    document.addEventListener("keydown", onKeyDown);
    cleanups.push(() => document.removeEventListener("keydown", onKeyDown));
  }

  if (closeOnOutsideClick) {
    const onPointerDown = (event: MouseEvent) => {
      const target = event.target as Node;
      const ignore = ignoreId ? document.getElementById(ignoreId) : null;
      const insideIgnored = ignore !== null && ignore.contains(target);
      if (!element.contains(target) && !insideIgnored) {
        onDismiss();
      }
    };
    // Defer one tick so the click that opened the overlay does not dismiss it.
    const timer = window.setTimeout(
      () => document.addEventListener("mousedown", onPointerDown),
      0,
    );
    cleanups.push(() => {
      window.clearTimeout(timer);
      document.removeEventListener("mousedown", onPointerDown);
    });
  }

  if (trapFocus) {
    const onKeyDown = makeTabTrap(element);
    element.addEventListener("keydown", onKeyDown);
    cleanups.push(() => element.removeEventListener("keydown", onKeyDown));
    focusableWithin(element)[0]?.focus();
  }

  openOverlays.set(elementId, () => cleanups.forEach((cleanup) => cleanup()));
}

export function close(elementId: string): void {
  const cleanup = openOverlays.get(elementId);
  if (cleanup !== undefined) {
    cleanup();
    openOverlays.delete(elementId);
  }
}
