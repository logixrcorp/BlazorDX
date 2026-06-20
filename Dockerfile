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

# Publishing the server host also builds the referenced libraries (triggering the Rust and
# TypeScript build targets) and the WASM client, collecting all static assets into wwwroot.
RUN dotnet publish samples/BlazorDX.Demo/BlazorDX.Demo/BlazorDX.Demo.csproj \
      -c Release -o /app/publish -p:UseAppHost=false

# ---- Runtime stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# curl is only for the container HEALTHCHECK.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/* \
 && useradd --create-home --uid 1001 app

WORKDIR /app
COPY --from=build /app/publish ./
RUN chown -R app:app /app
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    UseForwardedHeaders=true \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
  CMD curl -fsS http://127.0.0.1:8080/ >/dev/null || exit 1

ENTRYPOINT ["dotnet", "BlazorDX.Demo.dll"]
