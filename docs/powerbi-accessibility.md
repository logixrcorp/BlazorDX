# DxPowerBiReport accessibility statement

`DxPowerBiReport` embeds a Microsoft Power BI report in an interactive (WebAssembly /
Interactive Server) page. Two parties shape the result: **BlazorDX** (this wrapper)
and **Microsoft's Power BI client** (the report rendered inside its own iframe). This
note states where each line falls, so the conformance claim is honest about what it
can and cannot guarantee.

## What BlazorDX guarantees

- **A labeled, screen-reader-reachable container.** The embed container carries
  `role="application"` and a non-empty accessible name (`aria-label`, WCAG 4.1.2),
  derived from the `Label` parameter (default "Power BI report"). `role="application"`
  tells assistive technology this is an interactive widget with its own keyboard
  model — Power BI's.
- **Accessible loading and error states.** Before the embed completes, a
  `role="status"` `aria-live="polite"` region announces "Loading the report…". If the
  config fetch or the embed fails, a `role="alert"` panel surfaces the message — the
  circuit never crashes on a failed embed or a missing tenant.
- **Secure token handling.** The Azure AD token that authorizes the Power BI REST API
  is held and used **only on the server** (by `IPowerBiEmbedService`). The browser
  receives **only** the short-lived embed token + embed URL — which are *meant* for
  it; that is how Power BI "app owns data" embedding works. No AAD token is ever
  serialized into the config that crosses to the client.
- **`prefers-reduced-motion` honored.** The wrapper's own chrome carries no motion;
  the stylesheet also disables animations/transitions inside the frame for users who
  request reduced motion.
- **No untrusted markup.** The wrapper builds no `MarkupString` from runtime data; the
  embed config crosses to JavaScript as JSON over `[JSImport]` (never `IJSRuntime`,
  never raw HTML).
- **Clean teardown.** `DxPowerBiReport` is `IAsyncDisposable`; on dispose it unmounts
  the embed so no dangling iframe or handlers remain.

The automated axe gate (ADR 0012) covers the BlazorDX-owned chrome on `/powerbi`
(container, loading/error, any toolbar slot) — it passes with zero serious/critical
violations.

## What depends on Microsoft's Power BI client

These are **outside BlazorDX's control** and cannot be retrofitted by the wrapper:

- **The accessibility of the report body itself** — its visuals, tables, reading
  order, colour contrast, and chart alternatives — is produced by Microsoft's Power BI
  rendering inside the iframe and by how the report author built the report.
- **Keyboard navigation within the report** is provided by the Power BI client. The
  embedded report also offers a **"Show as a table"** view for individual visuals,
  which is Microsoft's accessibility surface for chart data; it is reached through the
  report's own visual menus, not through BlazorDX.
- **The embed iframe's internal chrome** (filter pane, page navigation) is Microsoft's
  UI; its conformance is Microsoft's, not BlazorDX's.

## Practical guidance

- Provide a meaningful `Label` so the container's accessible name describes the
  specific report ("Quarterly Revenue", not the default).
- Author Power BI reports for accessibility (alt text on visuals, sensible tab order,
  sufficient contrast). BlazorDX cannot add semantics the report does not emit.
- Point users at the report's "Show as a table" option for an accessible view of chart
  data — that is the Microsoft-provided equivalent.

## Production vs demo (the SDK)

In production, the wrapper (`dx-powerbi.js`) lazy-loads the real `powerbi-client` SDK
from a CDN and calls the genuine `powerbi.embed(...)`, which contacts
`app.powerbi.com` and requires a live Azure tenant + a valid embed token. The demo
and the E2E suite instead load a tiny **stub** `window.powerbi` (`powerbi-stub.js`)
that records the embed config and renders a placeholder — so the full embed loop is
provable with no tenant and no console errors. **The real `powerbi-client` render
against a live tenant is therefore documented but not verified in this repository.**
