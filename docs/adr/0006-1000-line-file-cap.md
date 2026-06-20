# ADR 0006 — 1000-line file cap

**Status:** Accepted

## Context

Large files accrete responsibilities, hide bugs, and produce unreviewable diffs.
A clear, enforced ceiling is a cheap forcing function for single-responsibility
files.

## Decision

No source file may exceed 1000 lines. Enforcement is at build time, not by
convention:

- `.cs` and `.razor`: the `DX1000` Roslyn analyzer (`analyzers/BlazorDX.Analyzers`)
  reports a build error.
- `.rs`, `.ts`, `.css`: the `FileLength` MSBuild target (`build/FileLength.targets`)
  fails the build.

## Consequences

- Files stay scannable; the cap nudges authors to split by responsibility.
- A legitimately long generated artifact must live outside the tracked source set
  (the generators emit into `obj/`, which is not checked).
- The number is deliberately round; the goal is the discipline, not the exact value.
