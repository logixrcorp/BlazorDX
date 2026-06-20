# Security Policy

BlazorDX is built secure-by-default — sanitizer-gated raw HTML, no singleton UI state, and
zero runtime reflection — but no software is perfect, and responsible reports are welcome.

## Reporting a vulnerability

**Please do not open a public issue for security problems.** Report privately to
**security@blazordx.com**.

Include the affected version or commit, a description, reproduction steps or a proof of
concept, and the impact you expect. We aim to acknowledge within **3 business days** and to
keep you updated as we investigate and ship a fix.

## Supported versions

Security fixes target the latest released minor version and `main`. Once a `1.0` release
exists, this section will list the supported version range.

## Disclosure

We follow coordinated disclosure: we will agree a fix and a disclosure timeline with you, and
credit you in the release notes unless you prefer to remain anonymous.

## Scope notes

- The libraries take **no third-party runtime dependencies** by design, which keeps the
  supply-chain surface small. Build-time toolchains (Rust, Node/esbuild) are pinned.
- Components route raw HTML only through the sanitizer; `MarkupString` is banned by an
  analyzer (`DX1001`). Report any path that renders unsanitized user input.
