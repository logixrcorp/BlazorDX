# Contributing to BlazorDX

BlazorDX is built to be read. The rules below are not style preferences — most
are enforced by the build, and a pull request that violates them will not pass.

## The 1000-line cap (hard rule)

**No source file may exceed 1000 lines.** This applies to `.cs`, `.razor`, `.rs`,
`.ts`, and `.css`.

- C# and `.razor` are checked by the `DX1000` Roslyn analyzer (build error).
- `.rs`, `.ts`, `.css` are checked by the `FileLength` MSBuild target (build error).

If a file is approaching the cap, that is a design signal: split it by
responsibility. A 1000-line file is almost never one concept.

## Human-readability rules

These keep the codebase navigable as it grows. The mechanical subset is enforced
by `.editorconfig`; the rest is enforced in review.

1. **One concept per file.** A file's name should tell you its single job.
2. **Descriptive names over abbreviations.** `columnIndex`, not `ci`.
   `rowPermutation`, not `rp`. The reader should not need a glossary.
3. **No clever one-liners.** If a line needs a comment to be understood, write it
   as several lines that do not.
4. **Comments explain *why*, not *what*.** The code already says what.
5. **Explicit over implicit.** Prefer obvious control flow to terse expression
   chains that hide a branch.

## Security rules (enforced by analyzers)

These are the reasons BlazorDX exists; the analyzer treats them as errors.

1. **No Singleton UI state.** Never `AddSingleton` a type that holds UI state — it
   is shared across all users on Blazor Server. Use the scoped-state helpers in
   `BlazorDX.Security`.
2. **No raw HTML.** Do not construct `MarkupString` from runtime data. If you must
   render HTML, pass it through `BlazorDX.Security`'s sanitizer.
3. **No reflection on hot or trimmable paths.** Use source generators
   (`BlazorDX.SourceGen`, `JsonSerializerContext`) instead. Reflection breaks AOT.

## Language boundaries

Put logic in the right tier (see [ARCHITECTURE.md](ARCHITECTURE.md) §3):

- **C#** — components, state, the public API.
- **Rust** (`BlazorDX.Compute.Rust`) — CPU-heavy algorithms only.
- **TypeScript** (`BlazorDX.Interop.Ts`) — DOM access only; keep it thin.

Cross the C# ↔ JS boundary with `[JSImport]`/`[JSExport]`, never `IJSRuntime`.

## Building and testing

```bash
dotnet build BlazorDX.slnx
dotnet test
```

The build compiles the Rust and TypeScript tiers automatically. Install the
prerequisites in [README.md](README.md) first. Warnings are errors here, so a
clean build is the bar.

## Commit and PR hygiene

- Keep diffs reviewable. The line cap helps; small focused PRs help more.
- A new component is not done until it has: a Tier 1 primitive, a Tier 2 styled
  wrapper, accessibility coverage, tests, and a demo page.
