# File manager — Concept → Code → Why

**Demo:** `/files` · **Source:** [`DxFileManager.cs`](../../src/BlazorDX.Components/DxFileManager.cs),
[`FileManagerPrimitive.cs`](../../src/BlazorDX.Primitives/Files/FileManagerPrimitive.cs),
[`file-dnd.ts`](../../src/BlazorDX.Interop.Ts/src/file-dnd.ts)

## Concept

A two-pane file manager: a folder tree on the left, the selected folder's contents
on the right. Items can be **moved** within or between folders, and OS files can be
**uploaded**. Every drag gesture has a drag-free equivalent, because drag-and-drop
is treated as an enhancement layered on top, never the only path.

## Code

- **Tier split (ADR 0001).** Logic/state — the mutable tree, move rules,
  breadcrumb, announcements — lives in the headless primitive
  [`FileManagerPrimitive.cs:39`](../../src/BlazorDX.Primitives/Files/FileManagerPrimitive.cs).
  The DOM + styling + native-DnD wiring lives in
  [`DxFileManager.cs:24`](../../src/BlazorDX.Components/DxFileManager.cs).
- **The single move primitive** both paths share:
  [`FileManagerPrimitive.cs:286`](../../src/BlazorDX.Primitives/Files/FileManagerPrimitive.cs)
  (`MoveAsync`) → [`TryMove` at `:327`](../../src/BlazorDX.Primitives/Files/FileManagerPrimitive.cs)
  rejects self/parent/descendant moves and name collisions.
- **Native HTML5 DnD** is wired in
  [`DxFileManager.cs:361`](../../src/BlazorDX.Components/DxFileManager.cs) (`OnAfterRenderAsync`
  registers draggables/drop targets by element id). The drop is only *accepted*
  because the JS bridge prevents `dragover`'s default —
  [`file-dnd.ts:69`](../../src/BlazorDX.Interop.Ts/src/file-dnd.ts).
- **Concurrency.** Rapid drops are serialized through a per-component guard so a
  fire-and-forget move can't corrupt the model:
  [`DxFileManager.cs:446`](../../src/BlazorDX.Components/DxFileManager.cs) (`QueueMove`).

## Why (accessibility + non-negotiables)

- **2.5.7 drag-free move alternative.** The ↔ "Move" toggle
  ([`DxFileManager.cs:310`](../../src/BlazorDX.Components/DxFileManager.cs), `aria-pressed`)
  arms a "mark then place" move; "Move here" targets are emitted on the breadcrumb
  ([`:107`](../../src/BlazorDX.Components/DxFileManager.cs)) and tree nodes
  ([`:222`](../../src/BlazorDX.Components/DxFileManager.cs)). The state machine is
  [`ToggleMoveCandidateAsync` / `PlaceMoveCandidateAsync` at `:256`/`:274`](../../src/BlazorDX.Primitives/Files/FileManagerPrimitive.cs).
  No drag, no pointer-path, no JS required for this path — which is the point.
- **Drag-free upload.** A standard `InputFile` (`<input type=file>`) at
  [`DxFileManager.cs:147`](../../src/BlazorDX.Components/DxFileManager.cs) is the
  always-available upload path; OS-file DnD merely enhances it.
- **4.1.3 status announcements.** A `role=status` / `aria-live=polite` region
  ([`DxFileManager.cs:326`](../../src/BlazorDX.Components/DxFileManager.cs)) announces
  every move/upload outcome (`AnnounceUpload` /`StatusMessage`).
- **2.4.3 focus management.** After a move, focus lands on the moved row if visible,
  else the status region — [`ApplyPendingFocusAsync` at `:511`](../../src/BlazorDX.Components/DxFileManager.cs).
- **Render mode (ADR 0013): hybrid.** Navigation/listing suits static-SSR; the
  move/upload/preview enhancements need interactive WASM + JS. The demo route runs
  `InteractiveWebAssembly`. See the "Hybrid" bullet in ADR 0013.

### Verified by

bUnit unit tests cover the primitive's move rules; the native-DnD acceptance gate
and the drag-free alternatives (which bUnit cannot drive) are covered by
[`FileManagerE2ETests.cs`](../../tests/BlazorDX.E2E.Tests/FileManagerE2ETests.cs).
