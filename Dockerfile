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

# Node.js 20 (esbuild bundling) + Rust (wasm32 compute kernel).
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates \
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

# Build BOTH native tiers explicitly into the interop static-asset folder. The in-build
# MSBuild targets degrade to a *warning* if cargo/esbuild don't run, which silently ships an
# image missing the wasm/JS the components import at runtime (the prod 404s). Doing it here
# fails the image build loudly instead, and guarantees the assets exist before publish.
RUN cargo build --release --target wasm32-unknown-unknown \
      --manifest-path src/BlazorDX.Compute.Rust/Cargo.toml \
 && cp src/BlazorDX.Compute.Rust/target/wasm32-unknown-unknown/release/dx_grid.wasm \
       src/BlazorDX.Interop/wwwroot/dx/dx_grid.wasm
RUN cd src/BlazorDX.Interop.Ts && npm ci && node build.mjs

# Publish the server host + WASM client, reusing the bundles built above (skip the
# graceful-degrade targets so a missing toolchain can't silently drop the assets).
RUN dotnet publish samples/BlazorDX.Demo/BlazorDX.Demo/BlazorDX.Demo.csproj \
      -c Release -o /app/publish -p:UseAppHost=false \
      -p:SkipTypeScriptBuild=true -p:SkipRustBuild=true

# Gate: the interop assets must be in the publish output. If not, print where they actually
# landed (so the failure is diagnosable) and fail — a broken image must never ship.
RUN if [ ! -f /app/publish/wwwroot/_content/BlazorDX.Interop/dx/grid-interop.js ] \
    || [ ! -f /app/publish/wwwroot/_content/BlazorDX.Interop/dx/dx_grid.wasm ]; then \
      echo '=== interop assets actually present in publish: ==='; \
      find /app/publish/wwwroot -iname 'grid-interop.js' -o -iname 'dx_grid.wasm' -o -iname 'grid-dom.js'; \
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
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    UseForwardedHeaders=true \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
  CMD curl -fsS http://127.0.0.1:8080/ >/dev/null || exit 1

ENTRYPOINT ["dotnet", "BlazorDX.Demo.dll"]
