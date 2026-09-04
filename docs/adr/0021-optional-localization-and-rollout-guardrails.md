# ADR 0021 — Optional localization, and the guardrails the rollout runs behind

**Status:** Accepted
**Extends:** [ADR 0016](0016-localization-rtl-strategy.md) (which stays accepted — the
mechanism it chose is unchanged; this record changes how components *consume* it, and adds
the enforcement 0016 named as future work)

## Context

ADR 0016 proved the mechanism on two pilots and deliberately deferred the rest: "the other
~130 components are a separate, later, multi-phase effort." Before starting that effort,
three research passes measured what it actually consists of. Two findings changed the plan.

**The roadmap's component count was wrong.** Of 142 `.cs` files in `BlazorDX.Components`,
57 have zero user-facing strings (headless wrappers, or parameter-driven components whose
text comes from the consumer). The real backlog is **83 components, ~250–270 unique
strings**, and it is extremely lopsided: 60 of the 83 have one to three strings each,
while 6 heavy hitters hold roughly 40% of all the text. RTL is 24 unconverted stylesheets
— 104 mechanical declarations plus 27 that need a judgment call, about half of which
should stay physical.

**The pilot's injection pattern does not survive being applied 83 times.** This is the
finding that forced a decision before any rollout batch, and it is the substance of this
ADR.

## Decision 1 — the localizer is resolved optionally, with English at the call site

ADR 0016's pilot injects the localizer directly:

```csharp
[Inject] private IStringLocalizer<DxAlert> L { get; set; } = default!;
```

Blazor resolves `[Inject]` properties **unconditionally at component activation**, before
any component logic runs and regardless of whether the string is ever read. A consumer who
has not called `AddLocalization()` therefore gets an `InvalidOperationException` when the
component is *created* — not English text, and not a message that names localization as the
cause.

With two components localized that is an obscure edge case. Across 83 it is a **mandatory
`AddLocalization()` call for the entire library**, introduced silently, one component at a
time, and documented nowhere outside an ADR. The friction was already measurable in this
repo's own test suite before the rollout even began: `GridStateTests`, `RemoteGridTests`,
`ObservabilityTests`, `RemoteGroupGridTests` and `DxFeedbackTests` had each added
`Services.AddLocalization()` while asserting nothing whatsoever about localization. Those
five were paying the tax for two localized components; every other consumer would have
paid it for 83.

So components no longer inject `IStringLocalizer<T>`. They hold a `DxStrings<T>`
(`src/BlazorDX.Components/DxStrings.cs`), which resolves the localizer **lazily and
optionally** through `IServiceProvider.GetService<T>()` — Blazor offers no optional-inject
attribute, so the service provider is the escape hatch — and falls back to English supplied
at the call site:

```csharp
[Inject] private IServiceProvider Services { get; set; } = default!;
private DxStrings<DxAlert>? s;
private DxStrings<DxAlert> S => s ??= new(Services);

// ...
builder.AddAttribute(16, "aria-label", S["Dismiss", "Dismiss"]);
builder.AddContent(147, S["ColumnsToggle", "Columns ({0}/{1})", VisibleColumnCount, Columns.Count]);
```

`AddLocalization()` becomes what it should have been from the start: **opt-in, and only for
consumers who want translations.** Registering nothing yields an English library that works.

### The fallback also fires on a missing resource, not just a missing localizer

ADR 0016 recorded a hazard it could not fix from inside the pilot: `IStringLocalizer`
returns *the key itself* when a lookup fails, so a typo or a missing `.resx` entry ships a
raw key into the UI — visible to end users, and invisible to every test that asserts
against the same key. `DxStrings` checks `LocalizedString.ResourceNotFound` and falls back
to the English argument, which converts that failure mode from "user sees `ColumnsToggle`"
into "user sees English." Across 250+ strings that is the difference between a broken
lookup being a defect and being a non-event.

### The trade-off, stated plainly

English now lives in two places: the call site and the invariant `.resx`. That duplication
is **inherent to having a code-level fallback at all** — a fallback that cannot be reached
without the resource system is not a fallback.

The alternative considered was reading the invariant `.resx` directly through
`ResourceManager` when no localizer is registered: one source of English, no duplication,
no DI. It was rejected because it introduces new runtime resource-loading machinery (and a
second, differently-behaving lookup path to keep AOT-safe) to remove a duplication whose
practical cost is low, and because inline English is *readable* — a reviewer sees what a
call site says without opening a `.resx` in another window. If the duplication ever drifts,
`DxStrings` fails safe: the `.resx` wins whenever it resolves.

## Decision 2 — DX1003 bans hardcoded user-facing strings, behind a ratchet

ADR 0016 named this analyzer as future work and sequenced it "after Phase 0, before the
multi-component rollout." Without it the rollout is a treadmill: unrelated feature work
adds unlocalized strings faster than batches remove them — demonstrably so, since the
scatter/bubble zoom work in [ADR 0020](0020-scatter-bubble-2d-zoom-strategy.md) added
about five new hardcoded strings while this plan was being written.

`HardcodedStringAnalyzer` (DX1003, Error) flags string literals reaching the user:
`builder.AddContent(n, "…")`, `builder.AddAttribute(n, "<user-facing attribute>", "…")`
for `aria-label`, `aria-description`, `aria-roledescription`, `aria-valuetext`, `alt`,
`placeholder` and `title`, and defaulted `[Parameter] string` properties **whose name says
they carry text** (`…Label`, `…Text`, `…Title`, `…Message`, `…Placeholder`, `…Description`,
`…Caption`, `…Heading`, `…Hint`, `…Tooltip`, `…Prompt`). It ignores literals with no
letters (`"✓"`, `"▾"`, `"×"`), format strings, and every machine-facing attribute
(`class`, `role`, `type`, …).

That last qualifier was not in the first draft, and CI caught the omission immediately:
flagging *every* defaulted parameter reported `DxAlert`'s own
`[Parameter] public string Severity { get; set; } = "info"` — a variant token that ends up
in a CSS class, not something a user reads. Matching on the parameter name is the same
shape as the attribute allow-list: wrong only by omission, never by false alarm.

**The ratchet.** DX1003 fires **only inside types that already hold a `DxStrings<…>`
member.** `TreatWarningsAsErrors` is repo-wide, so `Warning` severity is not an escape
hatch — an analyzer that fired everywhere would break the build on 83 components at once.
Localizing a component is therefore what switches its own guard on, each batch extends
coverage automatically, and the diagnostic count on the current tree is zero by
construction.

**The hole this leaves, stated rather than discovered later:** a brand-new component that
has never been localized is unguarded. The rollout's completion criterion is flipping
DX1003 to fire on every component file in `BlazorDX.Components`, which closes it.

### Amendment (first rollout batch): DX1003 sees less than the paragraph above claims

The first rollout batch contradicted that completion criterion, and the correction belongs
here rather than in a commit message. DX1003 inspects **literals at render call sites**, and
that turns out to be where a minority of BlazorDX's user-facing text actually lives. The
rest reaches the DOM through a variable:

- **Lookup tables** — `DxRichTextEditor` holds all 17 toolbar labels in a
  `static readonly (Command, Value, Glyph, Label)[]` and renders them in a loop.
- **Switch expressions** — `DxKbd`'s entire vocabulary (18 strings) is switch arms in
  `Display`/`Spoken`; the component has *no* user-facing literal at a call site at all, so
  DX1003 would have reported it fully compliant while every string was hardcoded.
- **Local helper arguments** — `Slider(builder, 20, "Brightness", …)`,
  `ZoomButton(builder, 122, "−", "Zoom out", …)`, `Header(builder, 74, "Name")`. This is the
  most common shape across the styled tier.

Flipping DX1003 to every component would not change this: the literal is not at a call site,
so there is nothing for the rule to see. Widening it to "any string literal in a localized
type" is not the fix either — it would flag CSS class names, element names, command
identifiers and format strings, which are the *majority* of literals in these files.

The answer is a **convention the analyzer cannot enforce but the consistency test can**:
indirect text still resolves through a literal `S["Key", "English"]` pair, just at the
lookup instead of at the render site (`DxKbd.Display` is the worked example). Because the
pair stays literal, `LocalizedStringConsistencyTests` — which does not care *where* in the
file the pair appears — validates it against the `.resx` exactly as it validates a direct
call site.

So the honest guarantee is three-part: **the analyzer catches regressions at render call
sites; the consistency test catches drift anywhere the convention is followed; nothing
mechanically catches a brand-new hardcoded literal introduced inside a helper method.** That
last gap is closed by review, and mitigated by every component's strings now being
enumerated in one `.resx` where an omission is visible.

### Second amendment: DX1003's defaulted-parameter rule demands the impossible

The rule flags `[Parameter] public string X { get; set; } = "…";`, and the fourth rollout
batch found 21 of them. It is a correct thing to flag — that default *is* user-facing English
— but the fix the rule implies cannot be written:

```csharp
[Parameter] public string Label { get; set; } = S["Loading", "Loading"];   // does not compile
```

A property initializer cannot reference an instance member, and `S` resolves through an
injected `IServiceProvider` that does not exist until activation. So this category needs a
different shape: the parameter defaults to `null` and the **render site** coalesces
(`Label ?? S["Loading", "Loading"]`).

That makes `string` into `string?` on 21 public parameters. The cost is that a caller reading
the property gets `null` rather than English; the benefit, beyond localization, is that
explicitly passing `null` now yields the default instead of an empty `aria-label` — a control
with no accessible name, which is the failure a screen-reader user would hit and a sighted
reviewer would not see.

Recorded here because the analyzer rule and the fix for it live in different places: someone
reading DX1003's message alone would try the initializer, watch it fail to compile, and have
no way to know the intended shape.

## Decision 3 — RTL is verified statically, by the same ratchet

Before this ADR, RTL was verified by exactly one check: `/dialog?dir=rtl` in
`AccessibilityE2ETests`. It runs axe, which is direction-agnostic — it asserts no serious
accessibility violations and **nothing about layout**. A stylesheet could be 100%
unconverted and pass it. Landing a 104-declaration sweep against that is unverified by
construction.

`RtlLogicalPropertyTests` scans shipped `wwwroot/*.css` for physical directional
properties (`margin-left`, `padding-right`, `border-left*`, `text-align: left|right`,
`float: left|right`, a bare `left:`/`right:`) and fails on any it finds — in files marked
`/* rtl-clean */`, and only those. Same ratchet as DX1003: each conversion batch adds the
marker to what it converts; the end state is every stylesheet marked.

A line carrying `/* rtl-exempt: <reason> */` is allowed, because **some physical usage is
correct**. `DxSheet`'s `Side="left"/"right"` names a physical screen edge; converting it
would make `Side="right"` dock on the left under RTL, contradicting the parameter its
consumer wrote. This formalizes, as an enforced convention, the carve-out ADR 0016's pilot
had written as prose in `dx-overlay.css`.

Two things the static guard cannot do are covered separately: `?dir=rtl` axe variants for
`/scheduler`, `/app/records` and `/charts` (the densest directional layouts), and one
end-to-end assertion that a browser really mirrors — `DxSelect`'s caret sits after its
value in LTR and before it in RTL, with the computed `direction` asserted on both pages so
the comparison cannot pass vacuously.

**Also closed:** no test anywhere set `CultureInfo.CurrentUICulture`, so the `.fr.resx`
files were validated only by the AOT job noticing a satellite assembly existed. A test now
renders under `fr` and asserts the French value, which is what actually proves the
`fr-FR` → `fr` → invariant chain resolves.

## Consequences

- New `DxStrings<T>` in `BlazorDX.Components`; `DxAlert` and `DxDataGrid` retrofitted
  (23 call sites). The `.resx` files are unchanged, and `DxDataGridResources` remains as
  the non-generic marker type — the default factory derives a resource name from the
  *closed* generic type, so `DxStrings<DxDataGrid<TRow>>` would look for a different
  resource per `TRow`.
- **`AddLocalization()` is no longer required to use BlazorDX.** Eleven test classes that
  had added it purely to keep components activating no longer call it; that removal is the
  regression test for this ADR.
- New analyzer DX1003 and new `RtlLogicalPropertyTests`, both scoped by an opt-in marker,
  both reporting zero on the current tree.
- New rollout runbook at [docs/localization.md](../localization.md), carrying the measured
  backlog and the batch order.
- Not covered here, and each needing its own mechanism: `FormModelGenerator` bakes English
  validation messages into generated C#; four Tier-1 primitives carry English defaults;
  `DxScheduler`'s weekday abbreviations and `DxFileManager`/`DxDatePicker`'s use of
  `InvariantCulture` for user-visible dates are a date-formatting defect that exists
  independently of localization; and chart RTL is unreviewed rather than done, since chart
  geometry is computed in C# render trees and never appears in `dx-chart.css`.
