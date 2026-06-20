# Security Policy

BlazorDX is built secure-by-default (sanitizer-gated raw HTML, no singleton UI state,
zero runtime reflection), but no software is perfect — responsible reports are welcome.

> Replace the placeholders (`<…>`) with your real contact before publishing.

## Reporting a vulnerability

**Please do not open a public issue for security problems.** Instead, report privately:

- Preferred: GitHub's **private vulnerability reporting** for this repository
  (Security → *Report a vulnerability*), or
- Email **`<security-contact-email>`**.

Include: affected version/commit, a description, reproduction steps or a proof of
concept, and the impact you expect. We aim to acknowledge within **`<N>` business days**
and to keep you updated as we investigate and fix.

## Supported versions

Until a `1.0` release, security fixes target the latest `main`. Once versioned releases
exist, this section will list the supported version range.

## Disclosure

We follow coordinated disclosure: we'll work with you on a fix and a disclosure timeline,
and credit you in the release notes unless you prefer to remain anonymous.

## Scope notes

- The libraries take **no third-party runtime dependencies** by design, which keeps the
  supply-chain surface small. Build-time toolchains (Rust, Node/esbuild) are pinned.
- Components route raw HTML only through the sanitizer; `MarkupString` is banned by an
  analyzer (`DX1001`). Report any path that renders unsanitized user input.
