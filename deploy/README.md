# Deploying blazordx.com

The BlazorDX demo + docs showcase, containerized and served through a **Cloudflare tunnel** —
no open inbound ports on the dev server, TLS terminated at Cloudflare's edge.

```
 visitor ──HTTPS──► Cloudflare edge ──tunnel──► cloudflared ──HTTP──► app:8080
                    (blazordx.com)              (container)          (Blazor Web App)
```

## What's here

| File | Purpose |
|------|---------|
| [`../Dockerfile`](../Dockerfile) | Multi-stage build: .NET 10 SDK + Rust (wasm32) + Node → publish; ASP.NET runtime image |
| [`docker-compose.yml`](docker-compose.yml) | Two services — `app` (Blazor) and `cloudflared` (tunnel) |
| [`.env.example`](.env.example) | Template for the tunnel token (copy to `.env`) |

## Prerequisites

- A Linux dev server with **Docker** + **Docker Compose v2** (`docker compose version`).
- The domain **blazordx.com** added to your Cloudflare account (any plan, including free).
- This repository checked out on the server (e.g. `git clone http://192.168.0.58:3300/eschlueter/BlazorDX.git`).

## 1. Create the tunnel in Cloudflare

1. Cloudflare dashboard → **Zero Trust** → **Networks** → **Tunnels** → **Create a tunnel**.
2. Type **Cloudflared**, name it (e.g. `blazordx`), **Save**.
3. On the install screen, **copy the token** — the long string in the shown
   `cloudflared service install <TOKEN>` / `... run <TOKEN>` command. You do **not** run that
   command yourself; the `cloudflared` container does, using the token from `.env`.
4. Still in the tunnel config, open the **Public Hostnames** tab → **Add a public hostname**:
   - **Subdomain:** *(leave blank)*  **Domain:** `blazordx.com`
   - (Add a second hostname with **Subdomain** `www` if you want www → same service.)
   - **Service type:** `HTTP`  **URL:** `app:8080`
   - Save. Cloudflare auto-creates the DNS record for the apex pointing at the tunnel.

> The service URL is `app:8080` because `cloudflared` resolves the compose **service name**
> `app` over the shared `edge` network. Plain HTTP here is correct — the public connection is
> HTTPS at the edge; the app trusts `X-Forwarded-Proto` (set via `UseForwardedHeaders=true`).

## 2. Configure the token

```bash
cd deploy
cp .env.example .env
# edit .env and paste the token from step 1
```

`.env` is gitignored — the token never lands in the repo.

## 3. Build and run

```bash
cd deploy
docker compose up -d --build
```

First build is slow (it installs Rust + Node and compiles the wasm/ESM/WASM client). Watch it:

```bash
docker compose logs -f app          # Blazor app startup ("Now listening on http://[::]:8080")
docker compose logs -f cloudflared  # tunnel registration ("Registered tunnel connection")
```

Then open **https://blazordx.com**.

## Updating after a `git pull`

```bash
cd deploy
git pull
docker compose up -d --build
```

## Operations

- **Health:** `docker compose ps` (the `app` container reports `healthy` once it serves `/`).
- **Restart just the app:** `docker compose restart app`
- **Stop everything:** `docker compose down`
- **Logs:** `docker compose logs -f`

## Troubleshooting

- **502 / tunnel can't reach origin** — the app isn't healthy yet (first build takes a while),
  or the dashboard service URL isn't `app:8080`. Check `docker compose logs app`.
- **Redirect loop / "too many redirects"** — `UseForwardedHeaders=true` must be set on the app
  (it is, in `docker-compose.yml`). Without it the HTTPS redirect fights the HTTP origin.
- **Tunnel won't connect on a restrictive network** — uncomment `TUNNEL_TRANSPORT_PROTOCOL:
  http2` in `docker-compose.yml` (some networks block QUIC/UDP 7844).
- **Rust/Node build failure** — the build needs outbound access to crates.io and the npm
  registry. Behind a proxy, configure Docker's build proxy settings.

## Notes

- AOT is off in the image for a lean, fast build; the showcase is fully functional (Rust
  compute still runs as wasm via the compute kernel). For the AOT build, add the `wasm-tools`
  workload to the Dockerfile build stage and publish with `-p:EnableAot=true`.
- No host port is published — the container is reachable **only** through the tunnel. To smoke-
  test locally before wiring DNS, temporarily add `ports: ["8080:8080"]` to the `app` service.
