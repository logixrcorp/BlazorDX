# syntax=docker/dockerfile:1
#
# BlazorDX demo + docs showcase (the public face of blazordx.com).
#
# The build is multi-toolchain because building the app also:
#   - compiles the Rust compute crate to wasm32   (cargo)
#   - bundles the TypeScript DOM bridge to ESM     (node + esbuild)
#   - publishes the Blazor WebAssembly client       (.NET SDK + wasm-tools)
# A Release publish natively relinks the WASM runtime (wasm-ld / wasm-opt), which needs the
# `wasm-tools` workload. AOT (per-method) is left OFF for a faster build; the showcase is fully
# functional. To ship the AOT build, publish with -p:EnableAot=true (expect a long compile).

# ---- Build stage ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Node.js 20 (esbuild bundling) + Rust (wasm32 compute + security kernels). build-essential
# (gcc + a linker) is needed to build/link Rust *build scripts* on the host triple — not for the
# wasm32 target itself, but a wasm32-unknown-unknown crate can still depend on a crate with a
# build.rs (e.g. BlazorDX.Security.Rust's crypto dependency chain: aes-gcm/p256/sha2 pull in
# generic-array, which has one). BlazorDX.Compute.Rust's dependency tree happens to have none, so
# it built fine without this; GitHub Actions' ubuntu-latest runner ships gcc preinstalled, which is
# why this gap never showed up in CI — only in a `docker build` from this minimal SDK base image.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates build-essential \
 && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
 && apt-get install -y --no-install-recommends nodejs \
 && curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y --profile minimal \
 && rm -rf /var/lib/apt/lists/*
ENV PATH="/root/.cargo/bin:${PATH}"
RUN rustup target add wasm32-unknown-unknown

# wasm-tools: the emscripten/wasm-opt toolchain a Release WASM publish relinks against.
RUN dotnet workload install wasm-tools

WORKDIR /src
COPY . .

# Build ALL native tiers explicitly into the interop static-asset folder. The in-build
# MSBuild targets degrade to a *warning* if cargo/esbuild don't run, which silently ships an
# image missing the wasm/JS the components import at runtime (the prod 404s). Doing it here
# fails the image build loudly instead, and guarantees the assets exist before publish.
# The interop asset folder isn't in the build context (.dockerignore strips its only files,
# the generated wasm/JS, leaving an empty dir Docker omits), so create it before writing.
# Two separate wasm32 crates: dx_grid (BlazorDX.Compute.Rust, the grid compute kernel) and
# dx_security (BlazorDX.Security.Rust, the ephemeral chat conduit's ECDH/AES-GCM crypto core).
# Missing either one silently breaks a different feature at runtime with no build-time signal
# unless it's built and gated here explicitly, same as dx_grid always was.
RUN mkdir -p src/BlazorDX.Interop/wwwroot/dx \
 && cargo build --release --target wasm32-unknown-unknown \
      --manifest-path src/BlazorDX.Compute.Rust/Cargo.toml \
 && cp src/BlazorDX.Compute.Rust/target/wasm32-unknown-unknown/release/dx_grid.wasm \
       src/BlazorDX.Interop/wwwroot/dx/dx_grid.wasm \
 && cargo build --release --target wasm32-unknown-unknown \
      --manifest-path src/BlazorDX.Security.Rust/Cargo.toml \
 && cp src/BlazorDX.Security.Rust/target/wasm32-unknown-unknown/release/dx_security.wasm \
       src/BlazorDX.Interop/wwwroot/dx/dx_security.wasm
RUN cd src/BlazorDX.Interop.Ts && npm ci && node build.mjs

# Publish the server host + WASM client, reusing the bundles built above (skip the
# graceful-degrade targets so a missing toolchain can't silently drop the assets).
RUN dotnet publish samples/BlazorDX.Demo/BlazorDX.Demo/BlazorDX.Demo.csproj \
      -c Release -o /app/publish -p:UseAppHost=false \
      -p:SkipTypeScriptBuild=true -p:SkipRustBuild=true

# Gate: the interop assets must be in the publish output. If not, print where they actually
# landed (so the failure is diagnosable) and fail — a broken image must never ship.
RUN if [ ! -f /app/publish/wwwroot/_content/BlazorDX.Interop/dx/grid-interop.js ] \
    || [ ! -f /app/publish/wwwroot/_content/BlazorDX.Interop/dx/dx_grid.wasm ] \
    || [ ! -f /app/publish/wwwroot/_content/BlazorDX.Interop/dx/dx_security.wasm ]; then \
      echo '=== interop assets actually present in publish: ==='; \
      find /app/publish/wwwroot -iname 'grid-interop.js' -o -iname 'dx_grid.wasm' -o -iname 'dx_security.wasm' -o -iname 'grid-dom.js'; \
      echo '=== _content tree (depth 3): ==='; \
      find /app/publish/wwwroot/_content -maxdepth 3 2>/dev/null | head -60; \
      echo 'ERROR: BlazorDX.Interop static assets missing from expected publish path'; exit 1; \
    fi

# ---- Runtime stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# curl is only for the container HEALTHCHECK. The .NET 10 base image already ships a
# non-root `app` user, so we reuse it rather than creating one.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build --chown=app:app /app/publish ./
RUN mkdir -p /keys && chown app:app /keys
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    UseForwardedHeaders=true \
    DOTNET_EnableDiagnostics=0 \
    DataProtection__KeysPath=/keys

# /keys holds the ASP.NET Core DataProtection key ring (antiforgery tokens, auth cookies).
# Mount a persistent volume here in production — without one, every container recreation
# (restart, redeploy) generates a fresh key ring and invalidates every outstanding browser
# session's antiforgery token, breaking in-flight form submissions with
# AntiforgeryValidationException. A bare `docker run` without `-v` still gets a *new* anonymous
# volume each time (same problem); reuse a named volume or bind mount across deployments.
VOLUME ["/keys"]

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
  CMD curl -fsS http://127.0.0.1:8080/ >/dev/null || exit 1

ENTRYPOINT ["dotnet", "BlazorDX.Demo.dll"]
