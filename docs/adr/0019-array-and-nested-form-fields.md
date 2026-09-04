# ADR 0019 — Array and nested-object form fields

**Status:** Accepted

## Context

[ADR 0018](0018-conditional-form-fields.md) shipped conditional fields but explicitly scoped out
array and nested-object fields, because both require redesigning `IFormModel<TModel>`'s core
contract (`string GetString(model, field)` / `void SetString(model, field, string)` — a
scalar-only interface both `DxForm`'s rendering and `FormTool`'s AI/MCP tool path depend on).
That redesign is this ADR.

Per explicit direction, this pass implements both array fields and nested-object fields
**together, in one pass**, not phased — the two are structurally intertwined anyway (an array
element is frequently itself a nested object), so solving them separately would mean solving the
harder combined case twice.

Two research passes grounded the design in the actual codebase rather than a green-field guess:

- **No existing Tier-2 component is reusable for collection editing.** `SortablePrimitive` (and
  its styled `DxSortableList`) is hardcoded to `IReadOnlyList<string>`, reorder-only, with no
  add/remove and no item template — confirmed by direct read. A new generic primitive was needed.
- **Zero precedent anywhere in this repo for a source generator handling a recursive/
  self-referential descriptor.** `ChartTreeNode`, `DxTreeView`'s `TreeNode`, and `DxTreeGrid`'s
  `ChildrenSelector` are all hand-authored, host-wired plain DTOs — none of the three existing
  generators (`GridAccessorGenerator`/`ChartAccessorGenerator`/`FormModelGenerator`) ever check
  whether a property's type is itself an attribute-tagged type needing its own generated
  accessor. This is genuinely first-principles design within this codebase.

The forms system's stated identity — "one model, two faces," a `DxForm` a person fills and an
MCP/AI tool a model calls, from the same generated descriptor — had to be honored here exactly as
it was for conditional fields: array/nested support had to reach `FormTool`'s schema and argument
application, not just rendering.

## Decision

### `IFormModelUntyped` — the one genuinely new idea

`IFormModel<TModel>` keeps every existing scalar member unchanged and now extends a new
non-generic interface, `IFormModelUntyped` (`src/BlazorDX.Primitives/Forms/FormModelUntyped.cs`):
`GetString`/`SetString`/`Validate` over `object` instead of `TModel`, plus nested/array accessors
(`GetNestedInstance`/`SetNestedInstance`/`GetNestedDescriptor`/`NewNestedInstance`,
`GetArrayStrings`/`SetArrayStrings`/`GetArrayInstances`/`SetArrayInstances`/
`GetArrayElementDescriptor`/`NewArrayElement`) — all with default no-op bodies, so a scalar-only
model, generated or hand-written, is completely unaffected by the addition; it simply never
overrides them.

This is the classic "generic interface derives from its own non-generic face" pattern, and it
exists for one reason: `DxForm`'s nested-field rendering and `FormTool`'s schema builder /
argument applier are infrastructure code that must recurse into a nested `[DxFormModel]` type's
own descriptor without knowing that type as a generic parameter at their own compile time — a
`FormTool.BuildInputSchema<TModel>` call for `Outer` cannot also be generic over every possible
`TNested` it might encounter. `Outer`'s generated implementation stays fully typed internally
(cast the boxed `object model` back to `Outer`, call `model.Address` directly, `new
AddressFormModel()` directly) and only boxes at the return boundary — **an ordinary upcast, not
reflection**; it never touches `Type.GetProperty`/`Activator.CreateInstance`/anything ADR 0002
forbids.

Two alternatives were considered and rejected:

- **A parallel `NestedFields`/`ArrayFields` collection** alongside the existing flat `Fields`
  list. Rejected: every consumer that renders "all fields in declared order" (`DxForm`'s
  auto-render loop, `FormTool.BuildInputSchema`) would have to interleave two lists back into
  order — real, avoidable complexity for no benefit. Object/Array-kind fields stay in the one
  flat `Fields` list, exactly like `Sensitive`/`DependsOn` rode along as extra data on the same
  record rather than a parallel structure.
- **Function/delegate-typed members directly on `FormFieldInfo`.** Rejected: `FormFieldInfo` is a
  plain, positional, value-equatable record of *static* field metadata; a nested-descriptor
  factory or array-element accessor is inherently *per-model-instance operational behavior* — it
  belongs on the descriptor (`IFormModel<TModel>`/`IFormModelUntyped`), not on the field's own
  static data record.

### The generator's cross-type reference needs no pipeline-ordering dependency

`FormModelGenerator` runs once per `[DxFormModel]`-tagged type — processing `Outer` and
processing `Address` are two independent invocations of the same `IIncrementalGenerator`
pipeline, with no dependency edge between them. When `Outer`'s transform runs, it reads
`Outer.Address`'s **declared type symbol** straight off the original user-written syntax tree via
the semantic model — available regardless of what order the two invocations happen to run in,
since it isn't derived from anything generated. Seeing `[DxFormModel]` on `Address`'s own symbol
(a plain attribute-presence check), `Outer`'s emitted code safely references
`global::Ns.AddressFormModel` on faith in the generator's own deterministic naming rule
(`{TypeName}FormModel`).

What actually resolves that reference is **not** one generator invocation reading another's
output — it's the C# compiler's own final semantic/emit pass, which runs only after *every*
generator invocation has added its output trees to the compilation. By then `AddressFormModel`
exists as an ordinary source file, and `Outer`'s reference to it binds like any other cross-file
C# reference. The one thing this requires as a new diagnostic: if `Address` doesn't actually
carry `[DxFormModel]`, has zero fields, or has no accessible parameterless constructor, that must
fail at *this generator's own* diagnostic-reporting level (checked purely from `Address`'s own
symbol — DX2008/DX2009 below), not surface as a raw compiler error from the final pass.

### No new attributes — `Kind` inference extends naturally

`FormFieldKind` gains `Object` and `Array` (appended, not inserted). A `[DxField]` property whose
*declared type itself* carries `[DxFormModel]` → `Object`, always — no
`[DxField(Nested = true)]` escape hatch in this pass (an explicit, stated v1 scope cut: if a real
need for "flatten this nested type as a single text field" surfaces later, it's an additive
attribute member, not a breaking change). A `[DxField]` property typed **exactly `List<T>`** →
`Array`; `T` either `[DxFormModel]`-tagged (array-of-nested) or a recognized scalar
(array-of-scalar, `FormFieldInfo.ArrayElementKind` set — `Choices` is reused, not duplicated, for
an array-of-enum's element choices).

`List<T>` only — not `T[]`, `IList<T>`, `ICollection<T>`, `IReadOnlyList<T>` — was chosen because
it's mutable, has a public parameterless constructor, and directly supports the "replace the
whole collection" mutation semantic this ADR settles on for both rendering and
`ApplyArguments`. Any other collection shape is reported as **DX2005**, closing the "silently
misclassified as Text" gap this design's own research found in the existing `Kind()` fallthrough.

### Generator: recursive-shape detection, five new diagnostics, one whole-graph pass

`FormModelAnalysis.ReadFields` gets a new branch, checked *before* the existing `Underlying()`
`Nullable<T>` unwrap (necessary — `List<T>` is a reference type and never goes through that
path), that detects `List<T>` and, separately, a property whose own type carries
`[DxFormModel]`. `FormFieldDef` gains matching `NestedTypeFqn`/`ArrayElementKind`/
`NestedDescriptorFqn` fields.

Five new diagnostics, contiguous with the existing DX2001–2004 block:

| ID | Trigger | Checked where |
|---|---|---|
| DX2005 | Array field's element type is neither `[DxFormModel]`-tagged nor a recognized scalar, or the property is some other collection shape | per-type `FormModelDiagnostics.Validate` — pure, from `FormFieldDef` data alone |
| DX2006 | Nesting/array reference cycle among `[DxFormModel]` types | new whole-compilation pass, `FormModelCycles` |
| DX2007 | `DependsOn` crosses a nested/array field boundary, either direction | per-type `Validate` |
| DX2008 | Nested/array-element target type has zero discovered fields | computed in `ReadFields`, from the live symbol |
| DX2009 | Nested/array-element target type has no accessible public parameterless constructor | computed in `ReadFields`, from the live symbol |

DX2005 and DX2007 fit the existing pure `FormModelDiagnostics.Validate(ImmutableArray<FormFieldDef>)`
shape (no compilation access needed — DX2007 in particular is not just a scope choice:
`FormFieldActivity.IsActive`'s `GetString(instance, dependsOn)` has no dotted-path traversal, and
this pass deliberately doesn't add one). DX2008/DX2009 need the *live* type symbol (to count its
own `[DxField]`/DataAnnotations-carrying properties, and check `InstanceConstructors`) — computed
directly in `ReadFields` while the symbol is still in hand, as **flat, single-level scans that
never recurse into the referenced type's own Object/Array fields**. This bounded-depth design is
deliberate: recursing to fully re-derive the referenced type's own field list would risk unbounded
recursion (a genuine `A↔B` cycle would recurse forever) — the flat scan sidesteps that hazard
structurally, rather than needing a depth limit.

DX2006 (cycle detection) is the one check that cannot be answered from a single type's own field
list — a real cycle is a genuine problem regardless: `FormTool`'s JSON-Schema builder recurses
over the **field-kind graph** (not live instance data), so an uncaught `A→B→A` cycle would recurse
**unconditionally infinitely**, regardless of what data anyone ever constructs. `FormModelGenerator.Initialize`
adds a `.Collect()` over the same per-type provider and a second, independent
`RegisterSourceOutput` that runs `FormModelCycles.Validate` — a pure 3-color (white/gray/black)
DFS over `TypeName → referenced NestedType FQNs`, directly unit-testable against hand-built
`FormModelDef` data, with no dependency on the existing per-type diagnostics/emission
`RegisterSourceOutput` (so per-type incremental caching is untouched).

**Testability gap, same resolution ADR 0018 already chose.** This repo still has no
`CSharpGeneratorDriver`-based isolated generator-test harness, and no `InternalsVisibleTo` from
`BlazorDX.SourceGen` to a test project (confirmed by search). DX2005/2007–2009 are covered
indirectly by the new `NestedFormFixtures.cs`/`NestedFormModelTests.cs` (they compile cleanly
because the fixture is well-formed — the generator's *positive* path is exercised). DX2006, which
needs a genuinely broken (cyclic) fixture to trigger, is verified once manually rather than
covered by an automated regression test — the same scoped, stated limitation ADR 0018 accepted
for DX2001–2004.

### Rendering needed a new split — `DxFormBody` — and two real bugs this caught

`FormFieldRenderer.Render` (`FormContext.cs`) gets two early branches, before today's
label/input/errors layout, so the existing scalar path (including template hooks) is completely
untouched code:

- **Object** → the existing `DxFormSection` wrapping a nested sub-form for the instance.
- **Array** → a new Tier-2 `DxFieldList<TItem>` (`TItem = string` for array-of-scalar, or the
  nested model type for array-of-nested). Array-of-nested rows are the Object-kind path above,
  repeated per element — deliberately not a third rendering mechanism.

**Two real problems were caught during implementation** (the first by CI, the second by review
before the next push), and together they forced a genuine architectural split, not a patch:

1. The first approach opened the nested sub-form as a dynamically-typed `DxForm<TNested>` via
   `builder.OpenComponent(int, Type)` with `typeof(DxForm<>).MakeGenericType(...)`. CI's
   warnings-as-errors build rejected this outright: `Type.MakeGenericType` carries
   `[RequiresDynamicCode]`, incompatible with Native AOT — and this repo publishes and
   smoke-tests an actual AOT build in CI (`AOT publish + smoke`). Not a style nitpick; a genuine
   "would break under `PublishAot`" hazard the analyzer caught honestly.
2. That same dynamically-opened `DxForm<TNested>` was rendering its own `<form>` element via its
   normal `BuildRenderTree` — nesting a `<form>` inside the outer form's own `<form>`, invalid
   HTML that produces unpredictable browser reparenting.

**The fix for both**: `DxForm<TModel>` is now a thin typed wrapper around a new internal,
**non-generic** component, `DxFormBody` — the actual field-rendering/validation engine, working
entirely over `IFormModelUntyped`/`object` rather than `TModel` (exactly the surface this ADR's
core decision exists to provide). `DxForm<TModel>` renders the real `<form>` element and opens one
`DxFormBody`, boxing `Model` and passing `Descriptor` (already an `IFormModelUntyped`, via
interface inheritance). A nested Object/Array field's rendering opens `DxFormBody` **directly** —
`builder.OpenComponent<DxFormBody>(...)`, a compile-time-known, non-generic type, so this is an
ordinary, ahead-of-time-compilable component instantiation, never `MakeGenericType`. And because
`DxFormBody` renders no wrapping element of its own (just a `CascadingValue<FormContext>` around
its fields), a nested `<form>` is structurally impossible now, not merely avoided by a flag.

A currently-null nested property is materialized (`new TNested()`, DX2009-guaranteed
constructible) and attached via `SetNestedInstance` *before* the sub-form renders, so there is no
write-back step — the sub-form edits the actual attached instance by reference identity.
Propagating the outer form's `Refresh()`/submit-time revalidation into nested sub-forms (each of
which keeps its **own independent** `errors` list — this is why a nested field's prefixed error
like `"Location.Street"` is never looked up via the outer form's `ErrorsFor`, only the outer
field's own unprefixed top-level message is) needs no interface or reflection at all: every
captured nested reference is the same concrete `DxFormBody` type, so `DxForm`/`DxFormBody` just
call `.Refresh()` on it directly.

**New Tier-1 primitive**, `CollectionEditPrimitive<T>` (`src/BlazorDX.Primitives/Interaction/`) —
built fresh rather than by extending `SortablePrimitive` (which is hardcoded to
`IReadOnlyList<string>`, reorder-only, and stays completely untouched). It composes the exact
same primitives `SortablePrimitive` already does — `ListReorder.Move<T>` and `RovingTabIndex` —
generically, adding the two genuinely new operations, `AddAsync`/`RemoveAtAsync`. The new Tier-2
`DxFieldList<TItem>` wraps it: row chrome (draggable, keyboard Alt+Arrow, the WCAG 2.5.7
▲/▼ single-pointer alternative) mirrors `DxSortableList`'s established convention exactly; the
remove button matches `DxChip`'s dismiss idiom (`type="button"`, `aria-label`, `×` content).

### `FormTool`: recursive schema, and `ApplyArguments`'s third pass

`BuildInputSchema`'s per-field loop is extracted into a private helper over `IFormModelUntyped`
(not the generic `IFormModel<TModel>`) so it can recurse into a nested/array-element descriptor's
own schema without generic gymnastics. DX2006 guarantees the type graph is acyclic for anything
that compiles, so this recursion terminates by construction; a defensive max-depth guard (16) is
added anyway as cheap insurance against a hand-rolled `IFormModelUntyped` implementer that
bypasses the generator.

`ApplyArguments`'s existing two-pass core (unconditional scalars, then conditional scalars
re-checked against the now-updated target) is similarly extracted and made recursively callable,
with a **new third pass** for Object/Array fields. Ordering relative to passes 1–2 doesn't matter
for correctness: DX2007 already forbids Object/Array fields from participating in `DependsOn` in
either direction, so nothing about pass-3 ordering can affect the first two passes' own
invariants. Object fields materialize-if-null then recurse. Array fields **replace the whole
collection on every call** — the simplest correct semantic, chosen deliberately over any
diff/merge-by-identity scheme, since a JSON tool-call payload has no natural per-element identity
key to merge against. Sensitive/DependsOn handling for nested sub-fields needs no special-casing:
the recursive call lands in the nested type's *own* independently-generated descriptor, which
already implements its own `Sensitive` skip and `DependsOn` evaluation — this is the direct payoff
of recursing over the shared `IFormModelUntyped` surface rather than writing separate
nested-aware logic.

## Consequences

- `IFormModel<TModel>` now extends `IFormModelUntyped`; every generated model gains three thin
  `object`-overload wrapper methods (`GetString`/`SetString`/`Validate` over `object`) — a
  mechanical, unavoidable consequence of the interface addition, since C# doesn't satisfy a
  differently-parameter-typed interface member by covariance. A scalar-only model's *existing*
  members are otherwise emitted byte-for-byte unchanged.
- New Tier-1 primitive `CollectionEditPrimitive<T>` and Tier-2 `DxFieldList<TItem>`
  (`src/BlazorDX.Primitives/Interaction/`, `src/BlazorDX.Components/`).
- Five new generator diagnostics (DX2005, DX2007–2009 per-type; DX2006 a new whole-compilation
  pass), alongside ADR 0018's DX2001–2004.
- A new internal, non-generic `DxFormBody` component — the actual field-rendering/validation
  engine behind `DxForm<TModel>` (which is now a thin typed wrapper around it) — not part of the
  public component API, but the vehicle that keeps nested rendering both AOT-safe (no
  `Type.MakeGenericType`) and free of the nested-`<form>` HTML hazard.
- **Known limitations, stated together:** array-of-scalar elements get no per-item constraint
  validation, only the list-level `Required` check (a real, separable feature, deferred
  deliberately); `List<T>` only, not `T[]`/`IList<T>`/other collection shapes; no
  `[DxField(Nested = true)]` escape hatch to flatten a nested type into a single field; DX2006 is
  verified by one manual compile-failure check, not automated regression tests, the same
  documented gap ADR 0018 already accepted for DX2001–2004.
- Closes the "Forms depth" roadmap item in full — conditional fields (ADR 0018) plus array and
  nested-object fields (this ADR) cover everything that item named.
