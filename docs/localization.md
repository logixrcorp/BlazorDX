# Localization & RTL — rollout runbook

How to localize a BlazorDX component and convert its stylesheet, and what is left to do.

The decisions behind all of this are in [ADR 0016](adr/0016-localization-rtl-strategy.md)
(the mechanism) and [ADR 0021](adr/0021-optional-localization-and-rollout-guardrails.md)
(optional resolution, DX1003, the RTL guard). This document is the procedure.

**Ground rule:** localization is **opt-in for consumers**. A consumer who never calls
`AddLocalization()` gets an English library that works. Nothing in this runbook may
reintroduce a hard dependency on the localization services.

---

## 1. Localizing a component

### The pattern

Three lines of boilerplate, then read strings through `S`:

```csharp
[Inject] private IServiceProvider Services { get; set; } = default!;
private DxStrings<DxAlert>? s;
private DxStrings<DxAlert> S => s ??= new(Services);
```

```csharp
// key, English fallback
builder.AddAttribute(16, "aria-label", S["Dismiss", "Dismiss"]);

// key, English fallback, composite-format arguments
builder.AddContent(147, S["ColumnsToggle", "Columns ({0}/{1})", VisibleColumnCount, Columns.Count]);
```

Do **not** inject `IStringLocalizer<T>` directly. It resolves at component activation
whether or not the string is read, so it makes `AddLocalization()` mandatory for anyone
rendering the component — see ADR 0021, decision 1.

The English text at the call site is the fallback used when no localizer is registered
**and** when the key has no resource entry. That second case matters: a bare
`IStringLocalizer` returns the key itself on a failed lookup, so a typo ships `ColumnsToggle`
into the UI. `DxStrings` returns the English instead.

### Text that isn't at a render call site

Most BlazorDX text is **not** a literal sitting in `AddContent`/`AddAttribute`. It reaches
the DOM through a variable, in three shapes:

| Shape | Example |
|---|---|
| Lookup table | `static readonly (Command, Value, Glyph, Label)[] Tools` in `DxRichTextEditor` |
| Switch expression | `DxKbd.Display` / `DxKbd.Spoken` |
| Local helper argument | `Slider(builder, 20, "Brightness", …)`, `Header(builder, 74, "Name")` |

**The rule: keep the `S["Key", "English"]` pair literal, and put it where the text is
*chosen* rather than where it is rendered.**

```csharp
// Switch arms resolve through the localizer; the method becomes an instance method.
private string Display(string token) => token.ToLowerInvariant() switch
{
    "ctrl" or "control" => S["KeyCapControl", "Ctrl"],
    "cmd" or "command"  => "⌘",   // no letters, no language — deliberately not localized
    // ...
};

// Helper arguments are wrapped at the call site, not inside the helper.
Slider(builder, 20, S["Brightness", "Brightness"], brightness, v => brightness = v);
Header(builder, 74, S["ColumnName", "Name"]);
```

This matters for more than tidiness. **DX1003 cannot see any of these** — the literal is not
at a call site — so the convention is what keeps them covered: because the pair stays
literal, `LocalizedStringConsistencyTests` validates it against the `.resx` wherever in the
file it appears. Hide the text behind a computed key (`S[keyVariable, label]`) and it drops
out of every check at once, and its resource entry then looks orphaned. See
[ADR 0021](adr/0021-optional-localization-and-rollout-guardrails.md), the amendment under
Decision 2.

Two consequences worth planning for:

- A `static` helper that returns user-facing text has to become an **instance** method,
  since `S` is an instance member.
- Give the display form and the spoken form **separate keys** even when the English
  collides. `DxKbd` renders "Ctrl" on the key cap and says "Control" to a screen reader; a
  language that abbreviates differently needs both, and sharing one key would make the
  screen reader announce the abbreviation.

### Defaulted `[Parameter]` text

A `[Parameter]` with an English default cannot use the ordinary pattern at all:

```csharp
[Parameter] public string Label { get; set; } = S["Loading", "Loading"];   // does NOT compile
```

A property initializer cannot call an instance member, and `S` is reached through the injected
`IServiceProvider`, which does not exist until the component is activated. So the parameter
defaults to `null` and the **render site** coalesces:

```csharp
[Parameter] public string? Label { get; set; }
// ...
builder.AddAttribute(3, "aria-label", Label ?? S["Loading", "Loading"]);
```

`string` becomes `string?`. That is a real change to the public surface, and worth being
deliberate about:

- A caller who **sets** the parameter sees no change.
- A caller who **reads** it now gets `null` instead of the English default. Component parameters
  are rarely read from outside, but this is the compatibility cost.
- A caller who explicitly passes `null` used to get an empty string rendered; now they get the
  localized default. That is a behaviour change, and the better behaviour — an empty
  `aria-label` is a control with no accessible name.

**Coalesce once.** When a component only forwards the parameter to another component that owns
the fallback, forward the `null` — `DxForm` passes `SubmitText` straight to `DxFormBody`, and
coalescing in both would localize twice and defeat a caller who deliberately passed nothing.

### The `.resx` files

Put them at the **project root**, next to the `.cs` file, named for the type:

```
src/BlazorDX.Components/DxAlert.resx      # invariant (English)
src/BlazorDX.Components/DxAlert.fr.resx   # French
```

**Never set `ResourcesPath`.** ADR 0016 lost time to this: with `ResourcesPath` set, the
default factory looks for a resource name that the compiler does not emit, every lookup
misses, and — because a miss returns the key — the UI shows key names while every test
still passes. Root-level placement is the configuration that works.

### Generic components need a non-generic marker type

The default factory derives the resource name from the **closed** generic type, so
`DxStrings<DxDataGrid<TRow>>` would look for `DxDataGrid'1[[Widget]]` — a different
resource for every `TRow`. Declare a marker type and localize against that:

```csharp
// One .resx shared by every TRow.
public sealed class DxDataGridResources;

private DxStrings<DxDataGridResources> S => s ??= new(Services);
```

The `.resx` is then `DxDataGridResources.resx`.

### Sharing one resource file across a family

A marker type can also be shared deliberately. All 15 charts localize against
`DxChartResources`, because fifteen `.resx` files averaging two entries would give a translator
fifteen files to open for one recurring sentence pattern.

Keep keys **component-specific** even in a shared file — `PieChartLabel`, not `Label`. Share a
key only when the English is genuinely identical (the four zoomable charts share `ResetZoom`
and `PointsCaption` verbatim). A key shared by two components means one component's translation
silently becomes the other's.

`LocalizedStringConsistencyTests` aggregates usage per *resource file*, so the orphan check
works across a shared one; it would otherwise report each chart's keys as unused by every other
chart.

### The two tests

Every localized component gets both, because each catches what the other cannot:

1. **A sentinel test** — register `FakeStringLocalizer<T>` (returns `§§KEY§§`) and assert
   the sentinel. This is the only assertion that distinguishes "routed through the
   localizer" from "still a hardcoded literal that happens to match", since the English
   fallback usually equals the English resource value.
2. **A real-resource test** — `Services.AddLocalization()`, assert the English value.
   This proves the actual `.resx` round-trips through the real factory rather than a fake.

`tests/BlazorDX.Components.Tests/DxAlertTests.cs` is the worked example, and adds the two
cases that only need to exist once: rendering with **no** localizer registered at all, and
rendering under `CultureInfo.CurrentUICulture = new("fr")` to prove the
`fr-FR` → `fr` → invariant chain resolves.

Only register a localizer in tests that assert something about localization. If you find
yourself adding `Services.AddLocalization()` to make an unrelated test render, something
has reintroduced the hard dependency — fix that instead.

### DX1003 turns itself on

Once a type holds a `DxStrings<…>` member, the `HardcodedStringAnalyzer` starts flagging
hardcoded user-facing literals **in that type**: `AddContent`, the user-facing attributes
(`aria-label`, `aria-description`, `aria-roledescription`, `aria-valuetext`, `alt`,
`placeholder`, `title`), and defaulted `[Parameter] string` properties whose name ends in
`Label`, `Text`, `Title`, `Message`, `Placeholder`, `Description`, `Caption`, `Heading`,
`Hint`, `Tooltip` or `Prompt`. It ignores letter-free glyphs (`"✓"`, `"▾"`), format
strings, and machine-facing attributes.

The parameter-name test is deliberate: `[Parameter] public string DismissLabel = "Dismiss"`
is text a user reads, while `[Parameter] public string Severity = "info"` is a variant token
that ends up in a CSS class. If you add a text-carrying parameter with a name outside that
list, add the suffix to `UserFacingParameterSuffixes` rather than working around the rule.

So a half-localized component fails the build. That is the intent: the analyzer's coverage
is exactly the set of components already converted, and it grows with each batch.

---

## 2. Converting a stylesheet

Replace physical directional properties with logical ones:

| Physical | Logical |
|---|---|
| `margin-left` / `margin-right` | `margin-inline-start` / `margin-inline-end` |
| `padding-left` / `padding-right` | `padding-inline-start` / `padding-inline-end` |
| `border-left` / `border-right` | `border-inline-start` / `border-inline-end` |
| `border-top-left-radius` | `border-start-start-radius` |
| `text-align: left` / `right` | `text-align: start` / `end` |
| `left:` / `right:` (positioning) | `inset-inline-start` / `inset-inline-end` |

Then add the marker to the file header, which is what enables enforcement:

```css
/* rtl-clean — converted to logical properties; see docs/localization.md */
```

`RtlLogicalPropertyTests` checks **only** marked files. An unmarked stylesheet is not
failing, it is simply not converted yet.

### When physical is correct

Some directional CSS should stay physical, and the guard has an escape hatch for it:

```css
inset-inline-start: 0;
right: 0; /* rtl-exempt: DxSheet Side="right" names a physical screen edge — flipping it
             would dock the sheet left under RTL, contradicting the parameter */
```

`src/BlazorDX.Components/wwwroot/dx-overlay.css` is the worked example: converted, marked,
and with every deliberately-physical line carrying a reason. The two categories that
legitimately stay physical are **APIs that name a screen edge** (`DxSheet`'s `Side`) and
**symmetric boxes** pinned to both edges.

Flip the showcase with `?dir=rtl` on any route to check the result by eye.

---

## 3. What is left

Measured, not estimated. (The roadmap previously said "~130 components"; that counted every
file rather than the ones with user-facing text.)

| | Count |
|---|---|
| `.cs` files in `BlazorDX.Components` | 142 |
| …with zero user-facing strings (headless / parameter-driven) | 57 |
| **Components to localize** | **83** (~250–270 unique strings) |
| …localized so far | **40** — 2 pilots + 6 heavy hitters + 15 charts + 17 more |
| …strings externalized so far | 204, across 26 resource files |
| …with 1–3 strings each | 60 |
| …heavy hitters (>10 strings) | 6, holding ~40% of all text |
| Stylesheets converted | **25 of 25 — done** |
| RTL declarations remaining | 0 (104 mechanical rewrites + 22 judgment calls, applied) |

### Batch order

**Strings** — the 6 heavy hitters individually → the 4 near-identical zoomable charts as one
batch → the ~20 remaining charts sharing one `DxChartResources.resx` → 11 editorial
components → ~28 stragglers alphabetically.

Batch 1 landed 4 of the 6 heavy hitters — `DxKbd` (18), `DxImageEditor` (18),
`DxDocumentViewer` (18), `DxFileManager` (14): **68 strings**.

Batch 2 finished the other two — `DxRichTextEditor` (38) and `DxScheduler` (15): **53
strings**, plus `DxScheduler`'s date-formatting fix, which was done in the same pass because
externalizing "Previous month" while every date still rendered through `InvariantCulture`
would have been half a job.

Batch 3 took the whole chart family in one pass — 15 components, **22 strings**, all against a
single shared `DxChartResources.resx`. `DxGraph`, `DxLinearGauge` and `DxRadialGauge` turned out
to carry no user-facing text at all and were left alone.

Batch 4 closed the defaulted-`[Parameter]` category — 21 parameters across 18 components, plus
every other string in those same files so none was left half-localized (a half-localized
component fails the build, since DX1003 arms itself the moment a `DxStrings` field appears).

**40 of 83 components, 204 strings.** The remaining 43 hold roughly 60–80 between them.

**CSS — finished.** All 25 stylesheets carry `rtl-clean`, and
`RtlLogicalPropertyTests.Every_shipped_stylesheet_is_marked_converted` now makes the marker
**mandatory**, so a new stylesheet cannot opt out of the check by omitting it. That was the one
hole an opt-in ratchet always leaves, and closing it is what "done" means here.

Four physical usages that a rewrite would have got wrong, kept as worked examples of the
`rtl-exempt` reasoning:

- `DxSheet`'s `Side="left"/"right"` names a **physical screen edge** — flipping it would make
  `Side="right"` dock left, contradicting the parameter its caller wrote.
- Boxes pinned to **both** edges (`left: 0; right: 0`) are symmetric; converting them is churn.
- `transform` has no logical form. `DxSwitch`'s thumb travel and the reading-progress bar's
  `transform-origin` needed explicit `[dir="rtl"]` rules — the two places where CSS itself
  cannot express the flip.

**Completion criterion:** DX1003 fires on every file in `BlazorDX.Components` (43 components
still to localize), and every stylesheet carries `rtl-clean` (**met**). Both ratchets exist to
be removed; one now is.

### Separate tracks, each needing a different mechanism

- **`FormModelGenerator`'s validation messages** bake English into generated C#; a
  localizer is not reachable from where they are emitted.
- **Tier-1 primitive English defaults** — four primitives carry user-visible English even
  though ADR 0001 says primitives are unlabeled.
- **Date and number formatting** — `DxScheduler` is **done** (batch 2); `DxDatePicker` and
  any other `InvariantCulture` formatting of user-visible values remain. This is a defect on
  its own terms, independent of translation: a French user reading "Monday, June 1" and
  "2:30 PM" is not a missing-translation problem.

  The rule established in batch 2: **culture data comes from .NET, not from a `.resx`.**
  Weekday and month names, date and time patterns, and number formats are already carried
  per-culture by the framework, so `CultureInfo.CurrentCulture.DateTimeFormat` is the source
  — adding seven `Mon`…`Sun` resource strings would duplicate data the framework owns and
  get it wrong for cultures whose abbreviations are not three letters. Two traps:

  - `SomeEnum.ToString()` and `DayOfWeek.ToString()[..3]` are **English identifiers**. They
    read as words, which is what makes them easy to render by accident.
  - `InvariantCulture` is still correct for **machine-facing** strings. `DxScheduler` keeps
    exactly one: a CSS `style` value, where the decimal separator must be `.` in every
    locale. Do not sweep those.

  Tests that assert formatted output become machine-dependent the moment a component honours
  the culture, so use `CultureScope` (`tests/BlazorDX.Components.Tests/CultureScope.cs`):
  `CultureScope.Invariant()` to pin an assertion, `CultureScope.For("fr-FR")` to prove the
  culture is actually honoured.
- **Chart RTL** — `dx-chart.css` scores zero physical properties because chart geometry is
  computed in C# render trees. That means unreviewed, not done.
