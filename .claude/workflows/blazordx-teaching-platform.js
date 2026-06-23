export const meta = {
  name: 'blazordx-teaching-platform',
  description: 'Close BlazorDX fundamentals gaps and add a self-taught learning layer',
  phases: [
    { title: 'Recon',   detail: 'Map repo paths, nav, existing patterns (read-only)' },
    { title: 'Design',  detail: 'Plan each of the 5 workstreams in parallel (read-only)' },
    { title: 'Build',   detail: 'Implement workstreams sequentially on one tree' },
    { title: 'Verify',  detail: 'Adversarial checks: trim, tests, E2E, coverage critic' },
    { title: 'Learn',   detail: 'Assemble the reference-style learning layer + docs' },
  ],
}

const REPO = 'D:\\Projects\\BlazorDX'
const RULES = `Constraints (never violate): zero runtime reflection on hot/trimmable paths
(binding stays source-generated; Release publish must keep ZERO IL trim warnings);
component-scoped state only (no Singleton UI state, analyzer DX1002); no raw MarkupString
unless sanitized via BlazorDX.Security; 1000-line file cap (DX1000 + FileLength target);
build with 'dotnet build BlazorDX.slnx', keep 'dotnet test' green, add Playwright E2E for
new browser behavior, verify in a real browser. One ADR per architectural decision.
Work in ${REPO}.`

const WORKSTREAMS = [
  { key: 'persist', title: 'Prerender state persistence',
    spec: `Add declarative [PersistentState]/PersistentComponentState (reusable primitive,
    not per-component boilerplate) so data-fetching components run their fetch ONCE across
    prerender+hydration. Acceptance: demo page with a visible fetch counter; E2E asserting
    no duplicate fetch on hydration.` },
  { key: 'rendermodes', title: 'All render modes + lifecycle x-ray',
    spec: `Add demo pages under Static SSR, InteractiveServer, InteractiveWebAssembly,
    InteractiveAuto, each labeled with trade-offs, plus a lifecycle x-ray page logging
    SetParametersAsync->OnInitialized(Async)->OnParametersSet(Async)->OnAfterRender(Async)
    with firstRender, showing the prerender double-execution and the persistence fix.
    Acceptance: a 'Render Modes & Lifecycle' nav section; E2E smoke test per mode.` },
  { key: 'htmx', title: 'HTMX tier to fundamentals',
    spec: `Extend DxHtmxForm/helpers without breaking HATEOAS/LoB: HX-Request detection
    (partial to htmx, full page to direct nav); antiforgery re-enabled for htmx POSTs
    (replace demo .DisableAntiforgery()); verbs get/put/patch/delete; hx-trigger modifiers
    (delay/throttle/changed/revealed/poll); hx-swap-oob multi-target; htmx-indicator +
    double-submit prevention; one response-header demo (HX-Trigger/HX-Retarget). Acceptance:
    debounced active-search demo, OOB multi-update demo, inline-validation fragment demo,
    working no-JS full-page fallback; E2E for swaps and the no-JS path.` },
  { key: 'rust', title: 'Rust edition 2024 polish',
    spec: `Bump BlazorDX.Compute.Rust to edition 2024 / Rust 1.85+, fix lints (keep the
    unsafe extern "C" FFI boundary clean under stricter unsafe_op_in_unsafe_fn), confirm
    crate tests pass and the managed C# twin stays semantically identical.` },
]

phase('Recon')
const recon = await agent(
  `Read-only recon of the BlazorDX repo at ${REPO}. Report concrete facts the build
  agents will need: exact project/folder paths; how demo pages + nav are registered
  (file paths); current render-mode usage; where htmx is wired (DxHtmxForm, demo
  endpoint, App.razor script tag); the Rust crate + Cargo.toml location; how in-app
  doc/source assets are served today; how E2E tests are structured. Quote file:line.
  Output a compact reference map. ${RULES}`,
  { phase: 'Recon' })

phase('Design')
const designs = await parallel(WORKSTREAMS.map(w => () =>
  agent(`Using this repo map:\n${recon}\n\nProduce a concrete, file-level implementation
  plan for workstream "${w.title}". Spec: ${w.spec}\nList exact files to add/edit, the
  approach, risks, and the demo + E2E + learn-doc you will add. Read-only — plan only.
  ${RULES}`, { label: `design:${w.key}`, phase: 'Design' })
    .then(plan => ({ ...w, plan }))))

phase('Build')
// Sequential on a single working tree to avoid collisions on shared nav/docs files.
const built = []
for (const w of designs.filter(Boolean)) {
  const result = await agent(
    `Implement workstream "${w.title}" in ${REPO}.\nRepo map:\n${recon}\nYour plan:
    \n${w.plan}\n\nAlready-completed workstreams this run: ${built.map(b => b.title).join(', ') || 'none'}.
    Implement fully: code + demo page (wired into nav) + Playwright E2E + a docs/learn
    "Concept->Code->Why" page deep-linking the exact file:line you implemented. Then build
    (dotnet build BlazorDX.slnx) and run the relevant tests; fix failures before finishing.
    ${RULES}`,
    { label: `build:${w.key}`, phase: 'Build' })
  built.push({ title: w.title, result })
  log(`Built: ${w.title}`)
}

phase('Verify')
const VERIFIERS = [
  `Run 'dotnet publish -c Release' for the WASM client with TrimmerSingleWarn=false and
   report ANY IL trim warning verbatim (zero is required). Try to prove reflection crept in.`,
  `Run 'dotnet build BlazorDX.slnx' and 'dotnet test'; report every failure and any DX1000/
   DX1002 analyzer or FileLength (1000-line) violation introduced by this run.`,
  `Drive the demo with Playwright across Chromium/Firefox/WebKit: each render-mode page,
   the htmx swap round-trips, and the no-JS fallback. Report broken behavior with evidence.`,
  `Coverage critic: against the Blazor/Rust/htmx fundamentals, what is still missing,
   unverified, or only stubbed after this run? Be specific and adversarial.`,
]
const verdicts = await parallel(VERIFIERS.map((p, i) => () =>
  agent(`Adversarially verify the BlazorDX changes in ${REPO}. ${p}\n${RULES}`,
    { label: `verify:${i}`, phase: 'Verify' })))

phase('Learn')
const learn = await agent(
  `Assemble the self-taught learning layer in ${REPO}. Repo map:\n${recon}\nBuilt:
  ${built.map(b => b.title).join(', ')}\nVerifier findings:\n${verdicts.filter(Boolean).join('\n---\n')}
  \nCreate docs/learn/README.md as a lookup index (fundamental -> doc -> demo -> source line),
  with 'by tier' and 'by difficulty' views; ensure each fundamental has a "Concept->Code->Why"
  page; make every demo page show its own source in-app (reuse existing asset plumbing, no
  reflection); add the divergence lessons (source-gen forms vs EditForm; [JSImport] vs
  IJSRuntime) showing default beside BlazorDX's version. Fix any gaps the verifiers flagged
  that block the Definition of Done. Update README.md, docs/ARCHITECTURE.md, ROADMAP.md,
  COMPONENTS.md, and add ADRs. Re-run build + tests at the end. ${RULES}`,
  { phase: 'Learn' })

return { built, verdicts, learn }
