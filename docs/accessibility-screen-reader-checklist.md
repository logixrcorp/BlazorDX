# Manual screen-reader checklist (documents track)

The WCAG 2.2 AA gate ([ADR 0012](adr/0012-wcag-conformance-gate.md)) requires a
**manual screen-reader pass** per component — the half axe-core cannot cover. axe is
green in CI for every route; this checklist is the human verification that remains.

**Matrix:** NVDA + Chrome/Edge (Windows), JAWS + Chrome (Windows), VoiceOver + Safari
(macOS/iOS), TalkBack + Chrome (Android). Test with the monitor obscured where noted.

Mark each: ✅ pass / ⚠ issue (link) / ⬜ not yet run.

## All components (baseline)
- [ ] First-tab reveals a skip link; landmarks (`nav`/`main`) let the user jump blocks (2.4.1).
- [ ] Every interactive control announces **name, role, value/state** (4.1.2).
- [ ] Visible focus indicator is present and follows logical order (2.4.7 / 2.4.3).
- [ ] Async results are announced via the `aria-live` region (4.1.3).
- [ ] No keyboard trap; Esc dismisses overlays (2.1.2).
- [ ] `prefers-reduced-motion` suppresses transitions.

## File manager (`/files`)
- [ ] Tree announces `treeitem`, level, and expanded/collapsed state; arrow keys navigate.
- [ ] The **move alternative** is operable without a mouse: arm "Move", choose a "Move here"
      target, confirm the move and hear it announced (2.5.7).
- [ ] Upload via the standard file input works without drag (DnD is enhancement-only).
- [ ] After a move/upload, focus lands on a sensible target (moved row or status) (2.4.3).
- [ ] A name-collision is announced clearly, not silently dropped.

## Scheduler (`/scheduler`)
- [ ] View switch (Week/Month/Day) is a `tablist`; arrow keys move between tabs.
- [ ] Month grid announces as a grid with rows/cells; arrows + Home/End + PageUp/PageDown
      move the active cell; `aria-current="date"` reads on today.
- [ ] Day/Week time view: the active time slot is announced with date + hour.
- [ ] Each event announces title + date + time + category (category is **not** colour-only).
- [ ] View/date changes are announced (4.1.3).

## Document / PDF viewer (`/docviewer`)
- [ ] The embed/iframe announces a meaningful `title` (4.1.2).
- [ ] Toolbar (Download / Print / Open) is reachable and labelled; targets ≥24×24.
- [ ] The accessible **download link** is reachable even with the toolbar hidden.
- [ ] An unsafe/empty source shows a spoken "unavailable" placeholder, not a broken control.
- [ ] (PDF) Note whether the source is PDF/UA-tagged; untagged PDFs are a content limitation
      we surface via the download alternative, not something the viewer can retrofit.

## Reporting / Power BI (`/reports`, `/powerbi`) — Phase 4, shipped
- [ ] iframe `title`; our toolbar + parameter form conform (3.3.1 / 3.3.2).
- [ ] An accessible export (tagged PDF / data, or "Show as table") is offered.
- [ ] The accessibility statement names what we guarantee vs the renderer/author.

## Excel viewer/editor (`/excel`, `/excel-edit`) — pending manual pass
- [ ] Formula bar, cell reference, and active-cell value are announced on navigation.
- [ ] Arrow-key cell navigation is a `grid`/`gridcell` pattern, not a plain table read.
- [ ] Recalculated cells are announced (or the update is otherwise discoverable), not silent.

## Word viewer/editor (`/word`, `/word-edit`) — pending manual pass
- [ ] Document structure (headings, lists, tables) reads correctly through the OOXML↔HTML
      round-trip — verify no semantic loss versus the source `.docx`.
- [ ] The formatting toolbar's buttons announce pressed/active state for the current selection.

## HTMX static-SSR document viewer (`/htmx/doc`) — pending manual pass
- [ ] The no-JS fallback is independently operable (test with JS disabled, not just a slow AT pass).
- [ ] Full-page navigation (this route intentionally skips Blazor's enhanced nav) doesn't strand
      focus — confirm focus lands somewhere sensible after the reload.

## Editorial (`DxEditorial*`, `/insights/articles/...`) — pending manual pass
- [ ] Scrollytelling stages don't trap or steal focus, and the reveal transition respects
      `prefers-reduced-motion` for an AT/keyboard user, not just visually.
- [ ] Without the companion `dx-editorial-scrollytelling.js` script, stage content is still
      reachable and readable in document order — a screen reader shouldn't depend on the
      opacity/transform reveal to expose text that's already in the DOM.
- [ ] Heading hierarchy in a real article (h1 hero title → h2/h3 within the body) is correct and
      doesn't skip levels once `DxEditorialSpread`'s h2 and body h2/h3s are combined.
- [ ] `DxEditorialFigure`/`DxEditorialSpread` images have meaningful alt text in practice, not
      just in the one flagship article — this is an authoring discipline the component can't
      enforce.

> Record results (date, AT/browser versions, findings) in the PR that performs the pass. The four
> "pending manual pass" sections above were added when this checklist was last audited for drift —
> Phase 4 (Reporting/Power BI) already has recorded results; Documents (Excel/Word), the HTMX
> viewer route, and Editorial do not yet.
