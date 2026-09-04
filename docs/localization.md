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
`placeholder`, `title`), and defaulted `[Parameter] string` properties. It ignores
letter-free glyphs (`"✓"`, `"▾"`), format strings, and machine-facing attributes.

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
| …with 1–3 strings each | 60 |
| …heavy hitters (>10 strings) | 6, holding ~40% of all text |
| Stylesheets converted | 1 of 25 |
| RTL declarations remaining | 104 mechanical + 27 judgment calls |

### Batch order

**Strings** — the 6 heavy hitters individually (`DxRichTextEditor` ~30, `DxScheduler` ~25,
then `DxDocumentViewer` / `DxFileManager` / `DxKbd` / `DxImageEditor` ~18 each) → the 4
near-identical zoomable charts as one batch → the ~20 remaining charts sharing one
`DxChartResources.resx` → 11 editorial components → ~28 stragglers alphabetically.

**CSS** — 12 trivial files (≤4 hits each) → `dx-datagrid` → the four heavy files
individually → `dx-layout` last, since 7 of its 10 declarations are judgment calls.

**Completion criterion:** DX1003 fires on every file in `BlazorDX.Components`, and every
stylesheet carries `rtl-clean`. Both ratchets exist to be removed.

### Separate tracks, each needing a different mechanism

- **`FormModelGenerator`'s validation messages** bake English into generated C#; a
  localizer is not reachable from where they are emitted.
- **Tier-1 primitive English defaults** — four primitives carry user-visible English even
  though ADR 0001 says primitives are unlabeled.
- **Date and number formatting** — `DxScheduler` hardcodes weekday abbreviations, and
  `DxFileManager` / `DxDatePicker` format user-visible dates with `InvariantCulture`. This
  is a defect on its own terms, independent of translation.
- **Chart RTL** — `dx-chart.css` scores zero physical properties because chart geometry is
  computed in C# render trees. That means unreviewed, not done.
