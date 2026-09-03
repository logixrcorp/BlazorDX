# ADR 0018 — Conditional form fields

**Status:** Accepted

## Context

The completion roadmap's "Forms depth" item was "array / nested / conditional fields" — none
existed. Investigation found that array and nested-object fields both require redesigning
`IFormModel<TModel>`'s core contract (`string GetString(model, field)` /
`void SetString(model, field, string)` — a scalar-only interface both `DxForm`'s rendering and
`FormTool`'s AI/MCP tool path depend on). That redesign is scoped as separate, later work. This
ADR covers only conditional fields: showing/hiding, and requiring/not-requiring, a scalar field
based on another scalar field's live value in the same model — which does *not* require
touching the scalar contract.

The forms system's own identity, stated in `FormAttributes.cs`'s own doc comments, is "one
model, two faces": a `[DxFormModel]` type's generated descriptor powers both a human-editable
`DxForm` and an MCP JSON-Schema AI tool from the same field metadata. Shipping a field
capability only one face supports would be a real regression in that promise, not just an
omission — so this feature's AI/MCP path had to be kept in sync, not deferred.

## Decision

### Attribute-driven, generator-compiled — not a delegate or an expression string

A structured `[DxField(DependsOn = nameof(Other), DependsOnValue = "X")]` property, read by the
generator at compile time and emitted as a plain `if` — the same shape `Required`/`Min`/`Max`/
`Pattern` already use. Two alternatives were considered and rejected:

- **A `Func<TModel,bool>` delegate** assigned at the `DxForm`/model level. Simpler to wire, but
  it sidesteps the generator entirely: `FormTool.BuildInputSchema` only ever sees `model.Fields`
  — static, reflection-free data — never a runtime delegate, so it would have no way to discover
  or project the condition into JSON Schema without a second, parallel plumbing path.
- **A runtime-interpreted expression string** (a hand-rolled mini-language). Rejected as exactly
  the kind of "interpret behavior encoded in a string at runtime" ADR 0002 (zero-reflection,
  enforced by source generation) exists to forbid, for no benefit over a structured attribute
  given the deliberately narrow v1 operator set (below).

### One evaluator, called by every consumer

`FormFieldInfo` gains three plain trailing fields — `DependsOn`, `DependsOnValue`,
`DependsOnOperator` (`Equals`/`NotEquals`/`NotEmpty`) — the same trailing-optional-record-
parameter pattern `Sensitive` already established. A single hand-written (non-generated) static
helper, `FormFieldActivity.IsActive`, evaluates them by reading the dependency's current value
via the already-generated `GetString` — the same invariant-string form every other consumer of a
field's value already uses (bool → `"True"/"False"`, enum → member name, ...). `DxForm`'s
render loop, `DxFormField`, the generated `Validate`, and `FormTool.ApplyArguments` all call
this *same* method. There is exactly one implementation of "is this field currently active," not
one per consumer — the AI-facing path is not a second implementation of the rule, it's the same
one.

Comparisons are always over the invariant-string form, not typed C#, deliberately: it reuses
`GetString` as the single canonical read path, at the cost of case-insensitive-only comparisons
for text-driven conditions — an accepted limitation, since realistic driving fields are bool or
enum, not free text.

### Unified visibility + requirement, not decoupled

One condition governs both whether a field renders and whether its constraints — `Required`
included — apply. A decoupled model (a field that's always visible but only sometimes required)
is real and legitimate but was deliberately deferred, not overlooked: hidden fields aren't
required is the dominant convention for dynamic forms, and it halves the attribute surface for
v1. **Inactive fields are hidden, not rendered disabled** — simpler, avoids a second
accessibility surface (a visible-but-disabled field still needs `aria-disabled` treatment and
raises tab-order questions that don't need solving to prove the pattern). Not precluded from
becoming a later option — the render loop's `IsActive` guard could flip to a disabled-render
path without touching the primitive.

### Flat, single-field dependencies only — enforced at compile time

If a field's `DependsOn` itself names a field that has its own `DependsOn`, the generator
reports a compile **error**, not a silent gap — the same "prove the pattern, scope the rest out
explicitly" precedent ADR 0017 set for X-only chart zoom. Transitive activity (is the
intermediate field itself active, before its own condition is even meaningful) is a real feature
with real edge cases that don't need solving to prove conditional fields work; a hard compile-
time boundary means nobody discovers the gap by an app silently misbehaving in production.

`DependsOn` must `nameof()` another field that is itself unconditional, and must not be
`Sensitive`/`[AiHidden]` — an AI is never told a sensitive field exists, so it could never
legally satisfy a condition gated on one.

### New generator diagnostics — DX2001–DX2004

The first diagnostics any generator in this repo has reported (`GridAccessorGenerator.cs`/
`ChartAccessorGenerator.cs` report none). A fresh ID block, not `BlazorDX.Analyzers`' `DX10xx`
range — that project is a separate Roslyn analyzer, reported through a different mechanism;
keeping source-generator diagnostics visibly distinct avoids any future collision between the
two projects' ID spaces.

- **DX2001** — `DependsOn` doesn't name an opted-in form field on the type.
- **DX2002** — `DependsOn` names a `Sensitive`/`[AiHidden]` field.
- **DX2003** — `DependsOn` names a field that is itself conditional (chaining).
- **DX2004** — self-reference.

All `Error` severity, reported at the dependent property's own location, computed in
`FormModelAnalysis.Build` as a post-pass once the full field set is known (each rule needs to
see every field, not just the one being checked). On error, the offending field's `DependsOn` is
stripped before emission so the generated `.g.cs` stays syntactically valid — the reported
diagnostic is what the author sees, not a cascade of secondary compiler errors.

**Testability gap, and the resolution chosen instead of new infrastructure.** This repo has no
Roslyn `CSharpGeneratorDriver`-based isolated generator-test harness — existing "generator
tests" (e.g. `ChartRowGeneratorTests.cs`) compile a real annotated fixture in the same test
project and inspect the *output* at runtime; they structurally cannot assert on a diagnostic
that would make that same project fail to build. Building one (a new `BlazorDX.SourceGen.Tests`
project, mirroring `BlazorDX.Analyzers.Tests`' `ReferenceOutputAssembly=true` pattern, plus new
CI wiring) was considered and deferred as disproportionate to this pass. DX2001–2004 were
instead verified once manually — a deliberately-bad model confirmed to fail the build with the
expected message — documented as a scoped, stated limitation rather than covered by automated
regression tests.

### JSON-Schema shape: `allOf`/`if`/`then`, not `dependentRequired`/`dependentSchemas`

`FormTool.BuildInputSchema` declares no `$schema` draft today (verified by reading the method in
full), so adding newer keywords is not a version bump — a consumer that only reads
`properties`/`required` (today's whole shape) silently ignores the new keyword, and the field
looks exactly as unconditionally-optional as it does today. A graceful degrade for hosts that
don't evaluate conditionals at all.

Only fields that are both conditional *and* `Required` need a schema addition — a conditional-
but-optional field needs no signal, since "the AI may omit it" is already the default for any
optional field. For those, one `{"if":{"properties":{"<DependsOn>":<condition>}},"then":{"required":["<Field>"]}}`
is emitted, collected under a top-level `"allOf"` (JSON Schema permits only one `if` per schema
object, so multiple conditionally-required fields need `allOf`). `if`/`then` was chosen over
`dependentRequired`/`dependentSchemas`: those express *co-presence* ("if X is present, Y is
required"), not *value-conditioned* requirement ("only when X equals a specific value") — `if`/
`then` is the JSON-Schema-native idiom for the latter. `NotEquals` wraps the `const` in `"not"`;
`NotEmpty` degrades to `"minLength":1` — a documented, accepted divergence from the runtime's
whitespace-aware check, since JSON Schema has no direct equivalent.

A plain-English clause (`"Only applicable when <Label> is <value>."`) is also appended to the
dependent field's own `description`, regardless of whether it's required. This is not
redundant with the schema clause: many function-calling hosts (OpenAI, Anthropic) implement only
a documented subset of JSON Schema and are not guaranteed to evaluate `allOf`/`if`/`then` for
validation at all, while every host reads `description`. The natural-language nudge is the more
reliable signal to the model in practice — state this host-support caveat explicitly rather than
silently assume schema conditionals are universally enforced.

### `ApplyArguments` is the real enforcement boundary, independent of the schema

Rewritten as two passes: pass 1 sets every field with `DependsOn == null` from the supplied JSON
arguments (today's loop, filtered); pass 2 then evaluates `FormFieldActivity.IsActive` for each
conditional field against the *now-updated* target and only sets it if active, otherwise
silently skips it — the same posture the existing `Sensitive` field skip already uses (no new
error type, no special-cased message). This ordering matters: a conditional field's activity
must reflect *this same call's* values for its driving field, not a stale, previously-persisted
one. It is also what makes the flat/no-chaining compile-time rule load-bearing at runtime — pass
1 only ever applies unconditional fields, so a (compile-time-forbidden) chained `DependsOn`
could never read an unset pass-2 value; the DX2003 diagnostic exists precisely so this situation
is a compile error, never a runtime question.

## Consequences

- New Tier-1 primitive `FormFieldActivity` (`src/BlazorDX.Primitives/Forms/`) and a new
  `FormFieldDependsOnOperator` enum, extending `FormFieldInfo`/`DxFieldAttribute` with three
  trailing optional members each — source-compatible, following the exact precedent `Sensitive`
  already set.
- Four new generator diagnostics (DX2001–2004), this repo's first, in a new ID range separate
  from `BlazorDX.Analyzers`.
- `FormTool.BuildInputSchema`/`ApplyArguments` both changed in real, non-trivial ways — this is
  not a UI-only feature, consistent with the "one model, two faces" identity this ADR chose to
  honor rather than defer.
- **Known limitations, stated together rather than left implicit:** decoupled visibility/
  requirement is not supported (one condition governs both); dependency chains are a compile
  error, not supported at all; `DependsOn`/`NotEmpty` text comparisons are case-insensitive and
  whitespace-only values diverge slightly between the runtime check and the emitted schema's
  `minLength`; the schema's `allOf`/`if`/`then` clause is advisory, not guaranteed evaluated by
  every AI host — `ApplyArguments`'s two-pass skip is the actual boundary; the new diagnostics
  are verified by one manual compile-failure check, not automated regression tests, since this
  repo has no isolated Roslyn generator-test harness.
- Scoped to conditional fields only. Array and nested-object fields are unaffected and remain
  future work — this ADR does not claim to have solved "Forms depth" in full, only the piece
  that didn't require redesigning `IFormModel<TModel>`'s scalar contract.
