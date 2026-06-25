# DxReportViewer accessibility statement

`DxReportViewer` embeds or renders SQL Server Reporting Services (SSRS) reports in
the static-SSR + HTMX tier (ADR 0013). Two parties shape the result: **BlazorDX**
(this wrapper) and **the report itself** (Microsoft's HTML5 renderer plus the RDL
author). This note states where each line falls, so the conformance claim is honest
about what it can and cannot guarantee.

## What BlazorDX guarantees

- **A keyboard- and screen-reader-reachable wrapper.** The viewer root, the output
  region (`role="region"`, `aria-live="polite"`, focusable), and the export nav all
  carry conforming roles and accessible names.
- **An accessible name on the embed frame.** In embed mode the `<iframe>` always
  carries a non-empty `title` (WCAG 4.1.2), derived from the report path.
- **A conforming parameter form** (render mode). Every declared parameter gets one
  labeled control tied by `for`/`id` (WCAG 3.3.2); required parameters are marked
  visually and via `aria-required` (WCAG 3.3.1); declared **defaults are prefilled**
  and the user's last submission is re-prefilled (WCAG 3.3.7); a closed valid-value
  list renders as a constrained `<select>`.
- **The no-JavaScript floor.** The parameter form is a real `GET` to the host
  endpoint, so it works with scripting disabled; HTMX only *enhances* it by swapping
  the viewer fragment in place. The export links are real anchors.
- **A safe rendering boundary.** SSRS HTML5 output is untrusted markup and is
  **never** passed to a raw `MarkupString`. It flows through the injected
  `HtmlSanitizer` (the one sanctioned boundary, ADR 0007); the default policy is
  inert (HTML-encode-all). Credentials stay server-side (ADR 0010) and never appear
  in a browser-facing URL.
- **Accessible export.** PDF and CSV are offered as real, labeled links straight to
  the URL-Access render endpoint.

## What depends on Microsoft's renderer and the report author

These are **outside BlazorDX's control** and cannot be retrofitted by the wrapper:

- **The accessibility of the report body itself** — table header associations
  (`<th scope>`/`headers`), reading order, heading structure, and any chart or image
  alternative text — is produced by Microsoft's HTML5 rendering extension and the
  choices the RDL author made. A report authored without those will not become
  accessible just by being embedded.
- **Colour contrast** of the rendered report follows the RDL's styles.
- **The server toolbar and paging** in embed mode are Microsoft's UI, served inside
  the `<iframe>`; their conformance is the server's, not BlazorDX's.

## Practical guidance

- Prefer **render mode** with a sanitizing/allow-list `Sanitizer` policy when you
  need the BlazorDX wrapper to own as much of the surface as possible.
- Author reports for accessibility (header rows, tooltips/alt text, logical reading
  order). BlazorDX cannot add semantics the report does not emit.
- The automated axe gate (ADR 0012) covers the BlazorDX-owned chrome on `/reports`;
  the report body inside the iframe is audited by the report author's own process.
