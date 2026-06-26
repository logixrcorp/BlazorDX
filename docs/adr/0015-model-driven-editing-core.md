# ADR 0015 — Model-driven editing core for the document editors

**Status:** Proposed

## Context

The rich editors (`DxRichTextEditor`, and `DxWordEditor` layered on it) are built on
the browser's `contentEditable` plus `document.execCommand`. The DOM is the source of
truth: a command mutates the DOM, then we read the HTML back out, sanitize it, and
*re-derive* the `WordDocument` model from it (`WordHtml.FromHtml`). The model is
**downstream** of the editor, not authoritative.

This was the right call to ship quickly and stays trim/AOT-clean with no external JS
editor dependency. But building out the editor (gap fixes 1–6) made the ceiling concrete.
Every remaining capability that isn't a pure load/save round-trip hits the same wall:

- **No cursor/selection state we own.** We can't answer "which table/row/cell is the
  caret in?" — so table insert/delete, find-*next*/highlight, and a color-apply toolbar
  (color inputs steal the contentEditable selection) are all fiddly or blocked.
- **Undo/redo is the browser's**, and our model-level operations (find/replace re-mounts
  the editor) throw it away. There is no unified, predictable history.
- **`execCommand` is deprecated and browser-inconsistent** — "never fully specified, each
  browser has its own bugs" — so behavior and emitted markup vary (which is why
  `WordHtml.FromHtml` has to be lenient and why color/alignment parsing is heuristic).
- **Collaboration, track changes, and comments are out of reach** — they require a
  document model with stable positions and an operation/transaction log, which a
  DOM-as-truth design cannot provide.

The gap analysis confirmed the industry already crossed this bridge: CKEditor 5,
ProseMirror/Tiptap, Lexical, and Trix all **abandoned `contentEditable`+`execCommand` for
a model-as-source-of-truth core**, treating `contentEditable` as an I/O device — input is
converted into operations on an internal document model, and the DOM is rendered *from*
that model.

We already own the asset they each had to build: a clean, immutable `WordDocument`
model with a tested `.docx`/HTML round-trip. We are one architectural inversion away from
the modern design.

## Decision

**Invert the data flow: make `WordDocument` the authoritative source of truth and treat
`contentEditable` as an I/O surface.** The DOM is *rendered from* the model; user input is
captured as *intent*, translated into a **command** that produces a new immutable model
state, and the view re-renders (or is surgically patched) from the new model.

```
keystroke / paste / toolbar  ──►  intent  ──►  command  ──►  new WordDocument
        (DOM is input only)                                        │
                              selection model (owned by us) ◄──────┤
                                          view  ◄── render ────────┘
```

Principles, consistent with the rest of BlazorDX:

1. **The model is immutable and authoritative.** Edits are pure functions
   `WordDocument → WordDocument` (we already write these — find/replace, the planned
   table ops). This gives **undo/redo for free** as a stack of model states.
2. **We own a selection model** — `(blockIndex, runIndex, offset)` anchor/focus — derived
   from the DOM `Selection` on input, and pushed back to the DOM after a render. This is
   the missing piece that unblocks table editing, find-highlight, and color-apply.
3. **No `execCommand`.** Formatting becomes a command over the model + selection, not a
   browser command. `[JSImport]` interop shrinks to three primitives: read the current
   DOM selection, set the DOM selection, and (optionally) apply a minimal DOM patch.
4. **Zero new dependencies.** No ProseMirror/Lexical/CKEditor — those are large JS bundles
   that violate the trim-clean/AOT/headless/security posture (ADR-0007). We build the
   minimal core in C# over the model we already own; the browser side stays a thin TS
   bridge.
5. **Reuse, don't rewrite, the model and round-trip.** `WordModel`, `WordHtml`,
   `DocxReader`/`DocxWriter` are unchanged — they are the payoff of having kept the model
   separate all along.

### Migration strategy (incremental, behind a flag — not a big bang)

The risk is real, so this does **not** replace the working editor in one step:

- **Phase A — selection model + read-back.** Add the owned selection model and the
  `[JSImport]` get/set-selection primitives. Editor still uses `execCommand`; we only
  start *tracking* the caret. Low risk, immediately unblocks table-editing and
  find-highlight prototypes.
- **Phase B — model-driven commands behind `EditingCore="ModelDriven"`.** Re-implement
  formatting/typing as model commands for opt-in early adopters; keep the `execCommand`
  path as the default. Both render from the same model.
- **Phase C — undo/redo + history** on the model-state stack (replaces the brittle
  re-mount hack).
- **Phase D — flip the default** once parity + the WCAG E2E gate are green on the
  model-driven core; retire `execCommand`.
- **Phase E — capabilities the core unlocks:** in-place table editing, find-next/highlight,
  comments (additive metadata), then track changes and real-time collaboration (an
  operation log over the model).

Each phase ships behind a flag, is independently tested, and keeps the existing editor
working until parity is proven.

## Consequences

- **Unblocks the rest of the roadmap:** undo/redo, table-editing UI, find-next/highlight,
  color-apply, comments, track changes, and collaboration all become tractable — they were
  blocked on owning the selection + an operation model, which this provides.
- **Removes the deprecated `execCommand`** and its browser inconsistencies; emitted markup
  becomes deterministic (we render it), so `WordHtml.FromHtml` heuristics shrink.
- **Large, multi-phase effort with real risk** — re-implementing editing is non-trivial and
  must not regress the working editor or the WCAG-AA E2E gate; hence the flagged, phased
  rollout rather than a rewrite.
- **Stays true to the BlazorDX posture** — zero new dependencies, AOT/trim-clean, headless,
  security-by-default — unlike adopting a third-party JS editor, which was considered and
  rejected on exactly those grounds.
- **Out of scope remains out of scope** (ADR-0010): pagination/page-layout/headers-footers
  are *not* a goal of this core; the model is reading-order, LOB-oriented, and a11y-first.

### Alternatives considered

- **Stay on `execCommand` (status quo).** Rejected: permanently caps the editor below
  table-stakes (no robust undo, no collaboration) and rides a deprecated API.
- **Adopt a third-party JS editor (ProseMirror/Lexical/CKEditor).** Rejected: large JS
  payloads, an external dependency and supply-chain surface, and a second document model to
  reconcile with ours — all against ADR-0007 (security baseline) and the trim-clean/headless
  identity.
- **Model-driven core over the model we already own (this ADR).** Chosen: minimal,
  dependency-free, incremental, and it cashes in the model/round-trip investment.
