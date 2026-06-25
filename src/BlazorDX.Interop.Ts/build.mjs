// Bundles each TypeScript entry point to a minified ES module.
// Output directory is passed as argv[2] by the MSBuild TypeScript target;
// it defaults to the Interop project's static assets folder for local `npm run build`.
import { build } from "esbuild";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const outDir =
  process.argv[2] ?? resolve(here, "..", "BlazorDX.Interop", "wwwroot", "dx");

// One bundle per logical module the .NET side imports via JSHost.ImportAsync.
const entryPoints = [
  resolve(here, "src", "grid-interop.ts"),
  resolve(here, "src", "grid-dom.ts"),
  resolve(here, "src", "overlay.ts"),
  resolve(here, "src", "positioning.ts"),
  resolve(here, "src", "richtext.ts"),
  resolve(here, "src", "hotkeys.ts"),
  resolve(here, "src", "image-editor.ts"),
  resolve(here, "src", "file-dnd.ts"),
];

await build({
  entryPoints,
  outdir: outDir,
  bundle: true,
  minify: true,
  format: "esm",
  target: "es2022",
  sourcemap: true,
  logLevel: "info",
});

console.log(`BlazorDX: bundled ${entryPoints.length} module(s) to ${outDir}`);
