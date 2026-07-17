// Minimal Vitest config for the TypeScript DOM bridge. This project has no
// browser build step of its own to test against; specs run under jsdom
// (EventSource is faked per-spec since jsdom does not implement it) and mock
// the dx_security wasm exports from rust-loader.ts so the crypto call sequence
// can be driven deterministically. Real in-browser behavior (actual wasm,
// genuine closed Shadow DOM tamper detection, zeroing timing) is covered
// separately by tests/BlazorDX.E2E.Tests via Playwright.
import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    environment: "jsdom",
    include: ["test/**/*.test.ts"],
    watch: false,
  },
});
