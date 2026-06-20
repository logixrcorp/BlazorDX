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

# Build the TypeScript DOM bridges explicitly. The in-build MSBuild target degrades to a
# *warning* if esbuild fails, which would silently ship an image missing the JS interop
# modules (the _content/BlazorDX.Interop/dx/*.js the components import at runtime). Running
# it here fails the image build loudly instead, and produces the bundles before publish.
RUN cd src/BlazorDX.Interop.Ts && npm ci && node build.mjs

# Publish the server host (also builds the libraries + WASM client). SkipTypeScriptBuild
# reuses the bundles built above instead of re-running the graceful-degrade target.
RUN dotnet publish samples/BlazorDX.Demo/BlazorDX.Demo/BlazorDX.Demo.csproj \
      -c Release -o /app/publish -p:UseAppHost=false -p:SkipTypeScriptBuild=true

# Fail the build if the interop static assets didn't make it into the publish output, so a
# broken image can never reach production (this is exactly the failure that 404'd in prod).
RUN test -f /app/publish/wwwroot/_content/BlazorDX.Interop/dx/grid-interop.js \
 && test -f /app/publish/wwwroot/_content/BlazorDX.Interop/dx/dx_grid.wasm \
 || (echo 'ERROR: BlazorDX.Interop static assets (JS/wasm) missing from publish output' && exit 1)

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
