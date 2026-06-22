# BlazorDX

### 🌐 Live demo & docs → **[blazordx.com](https://blazordx.com)**

> An enterprise-grade, secure-by-default, headless component system for .NET 10 Blazor.
> MIT-licensed. Zero runtime reflection. C# + Rust + TypeScript, each used where it is strongest.

BlazorDX starts from a question that has gotten less attention in the Blazor
component space than productivity and feature count:

> *What would a Blazor component library look like if it were designed to be
> secure-by-default, AOT-safe, and styling-agnostic from the first line of code?*

It is not a productivity skin over Bootstrap. It is a layered system that compiles
down to the fastest primitives the browser offers and is designed, by policy, to avoid
the patterns behind two well-known failure modes in reflection-heavy component
architectures: breaking under trimming/AOT, and sharing UI state across users on the
server.

## What makes it different

| Principle | How BlazorDX does it |
| --- | --- |
| **Secure by default** | Component-scoped state only (no cross-user Singleton leakage), `ISafeAction` cancellation to kill out-of-order responses, raw HTML banned unless sanitized — all enforced by analyzers, not docs. |
| **Zero reflection** | JSON and grid binding come from C# source generators. Survives Native AOT and full IL trimming, where reflection-based binding can fail under trimming. |
| **Headless, two-tier** | Tier 1 primitives own behavior + accessibility; Tier 2 styled components own looks via CSS variables. Theme without fighting `!important`. |
| **Right language for the job** | C# for the render tree, **Rust → WASM** for heavy compute (sorting/filtering), **TypeScript → minified ESM** for the thin DOM bridge. |
| **Static-SSR + HTMX forms** | A hypermedia tier for forms and progressive enhancement with zero WASM payload and no SignalR circuit. |
| **AI-callable, securely** | One `[DxFormModel]` is both a form a person fills and a tool an assistant calls — served over the **Model Context Protocol**, gated by authorization, audited, and with sensitive fields kept out of the AI's reach. See [docs/ai-integration.md](docs/ai-integration.md). |
| **Readable forever** | Every source file is capped at **1000 lines**, enforced at build time. |

See [ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full blueprint,
[COMPONENTS.md](docs/COMPONENTS.md) for the component catalog,
[docs/adr](docs/adr) for the decisions behind it, and [ROADMAP.md](docs/ROADMAP.md) for what
"complete" means and what is still ahead.

**Live showcase:** [blazordx.com](https://blazordx.com) — every component, the docs, and the
DataGrid at 100k rows.

## Install

BlazorDX is published on [nuget.org](https://www.nuget.org/packages/BlazorDX.Components). Add the
one package you need — it transitively pulls the primitives, Rust/WASM compute, JS interop, and
the source generator:

```bash
dotnet add package BlazorDX.Components
```

Wire up the services (in **both** the WASM client *and* the server host `Program.cs`, so
prerendering works):

```csharp
builder.Services.AddBlazorDXCompute();               // grid compute (Rust in the browser) + DOM/JS interop
builder.Services.AddScoped<BlazorDX.Components.ToastService>();   // only if you use DxToast
```

Link the styles you use from the package's static web assets (start with the theme; add per
feature — the full set lives under `_content/BlazorDX.Components/`):

```html
<link rel="stylesheet" href="_content/BlazorDX.Components/dx-theme.css" />
<link rel="stylesheet" href="_content/BlazorDX.Components/dx-datagrid.css" />
<link rel="stylesheet" href="_content/BlazorDX.Components/dx-overlay.css" />
```

The Rust `dx_grid.wasm` and the JS bridges ship inside the package as static web assets and
load automatically — no extra build tooling on the consumer side.

> **Packages:** `BlazorDX.Components` (styled, start here) · `BlazorDX.Primitives` (headless +
> the source generator) · `BlazorDX.Interop` · `BlazorDX.Compute` · `BlazorDX.Security` ·
> `BlazorDX.Htmx` (static-SSR forms tier).

## Repository layout

```
src/        BlazorDX.* libraries (Primitives, Components, Interop, Compute, Security, SourceGen, Htmx)
analyzers/  BlazorDX.Analyzers — the build-time governance (DX1000 line cap + security bans)
build/      MSBuild targets that compile the Rust and TypeScript tiers
samples/    BlazorDX.Demo — a Blazor Web App with a live demo page per component
tests/      bUnit, compute, and analyzer test suites
docs/adr/   Architecture Decision Records
```

## Prerequisites

| Tool | Why | Install |
| --- | --- | --- |
| **.NET 10 SDK** | The whole platform. | https://dotnet.microsoft.com |
| **Rust** + `wasm32-unknown-unknown` | The Rust compute tier. | `rustup target add wasm32-unknown-unknown` |
| **Node.js** | esbuild bundles the TypeScript tier. | https://nodejs.org |

The build degrades gracefully: if Rust or Node is missing it emits a clear
warning and skips that tier, and the DataGrid still works because
`BlazorDX.Compute` ships a managed C# fallback for every Rust routine.

## Quick start

```bash
dotnet build BlazorDX.slnx          # builds C#, Rust (wasm), and TypeScript (esm)
dotnet test                         # bUnit + compute + analyzer suites (E2E skips if no server)
dotnet run --project samples/BlazorDX.Demo
```

To run the **real-browser end-to-end suite** (Playwright), start the demo, install a
browser once, then point the tests at it:

```bash
dotnet run --project samples/BlazorDX.Demo/BlazorDX.Demo &     # serves http://localhost:5296
pwsh tests/BlazorDX.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
BLAZORDX_BASEURL=http://localhost:5296 dotnet test tests/BlazorDX.E2E.Tests
```

The CI workflow (`.github/workflows/ci.yml`) defines the unit suites and the E2E suite
across Chromium, Firefox, and WebKit (GitHub Actions format; adapt for your CI host).

Then open the printed URL. The **DataGrid** page shows 100,000 rows, virtualized to
the viewport, sorted/filtered/aggregated by the Rust WASM module, with row
selection, inline edit, column reorder/resize, and pinned columns. The nav links to
every other component's demo — see [COMPONENTS.md](docs/COMPONENTS.md).

## Status

BlazorDX spans **~95 components** across overlays, inputs, navigation, the data-grid
family (flat / tree / pivot, with server-side data and grouping), data visualization,
scheduling (calendar / Gantt), editors (Markdown / WYSIWYG), files, forms (rendered from one
model that doubles as an AI/MCP tool), and an AI chat surface — every one built on the shared
headless engine and verified in a real browser. See [COMPONENTS.md](docs/COMPONENTS.md) for the
full catalog.

- **Tests:** bUnit + compute + analyzer suites green, plus the Rust crate's own unit
  tests, plus a **Playwright E2E suite** that drives a real browser against the running
  demo (native drag-and-drop, canvas pixels, focus, console errors) — covering what
  bUnit cannot, and wired into CI across Chromium/Firefox/WebKit.
- **Trimming/AOT:** `dotnet publish -c Release` trims the entire WASM client with
  **zero IL trim warnings** (`TrimmerSingleWarn=false` surfaces every one) — concrete
  proof the zero-reflection claim survives the linker, not just an `IsTrimmable`
  assertion. AOT compilation is opt-in via `-p:EnableAot=true` (needs the `wasm-tools`
  workload) and the CI `aot-publish` job builds the full component set AOT and smoke-tests
  the published app with Playwright.
- **Governance:** the 1000-line cap and security bans hold across every file.

## License & support

BlazorDX is **free and open source under the [MIT license](LICENSE)** — the full
component library, source generators, Rust compute, and tooling. No license keys, no
paid tiers gating the code, no runtime license checks. Use it in commercial products
without restriction.

Sustaining the project and serving teams that need more than community help is funded
through **optional support**, not by locking the code — see **[SUPPORT.md](.github/SUPPORT.md)**
for community help, sponsorship, and commercial support / SLA / consulting options.
Security reports: **[SECURITY.md](.github/SECURITY.md)**.

---

## Trademarks

BlazorDX is an independent project and is not affiliated with, endorsed by, or sponsored
by Microsoft, Progress/Telerik, DevExpress, Syncfusion, MudBlazor, Blazorise, or any other
company. All product names, logos, and trademarks are the property of their respective
owners; any reference to them is for identification and comparison only.
