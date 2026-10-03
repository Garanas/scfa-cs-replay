# Deployment — vault.jipwijnia.nl

The hosted viewer runs as a container behind Traefik on a small VPS (TransIP V1: 1 vCPU, 1 GB is
plenty: replays are parsed in the visitor's browser, the server only serves files and proxies the
OAuth token exchange). Measured locally: Traefik ~20 MB, the vault ~25 MB.

| File | What |
|---|---|
| `../Dockerfile` | Image: `FAForever.Replay.Server` with the viewer, on the chiseled ASP.NET runtime (non-root, port 8080). |
| `../.github/workflows/docker.yml` | CI: tests, then builds and pushes `ghcr.io/garanas/scfa-cs-replay:latest` and `:sha-<commit>` on every push to `main`. |
| `compose.yaml` | Production stack: Traefik (HTTPS via Let's Encrypt HTTP-01, HTTP → HTTPS) and the vault. |
| `compose.local.yaml` | Override to run the same stack locally from source, without Let's Encrypt. |
| `.env.example` | Settings for the server (`ACME_EMAIL`, `VAULT_HOST`, `VAULT_IMAGE`). |

## Try it locally

```sh
docker compose -f deploy/compose.yaml -f deploy/compose.local.yaml up --build -d
# http://vault.localhost
docker compose -f deploy/compose.yaml -f deploy/compose.local.yaml down
```

## First-time server setup

1. **VPS** with Ubuntu LTS. Log in with an SSH key, disable password login, enable
   `unattended-upgrades`, add a 1–2 GB swap file, and open only ports 22, 80 and 443 (`ufw`).
2. **Docker** Engine with the compose plugin (docs.docker.com/engine/install/ubuntu).
3. **DNS** at TransIP: an `A` record (and `AAAA` for IPv6) for `vault` pointing at the VPS. Leave the
   `MX` records of jipwijnia.nl alone — mail stays at TransIP.
4. **Image access**: the GHCR package must be public (GitHub → Packages → scfa-cs-replay → Package
   settings → visibility), or run `docker login ghcr.io` on the server with a read-only token.
5. **Stack**: copy `compose.yaml` and `.env.example` to e.g. `/opt/vault`, then
   ```sh
   cp .env.example .env    # fill in ACME_EMAIL
   docker compose up -d
   ```
   Traefik requests the certificate on the first HTTPS request once DNS points at the server.

## Updating

```sh
cd /opt/vault && docker compose pull && docker compose up -d
```

Roll back by setting `VAULT_IMAGE=ghcr.io/garanas/scfa-cs-replay:sha-<commit>` in `.env`.

## Adding another site

Add a service with its own labels, e.g. the main website:

```yaml
  website:
    image: nginx:alpine
    restart: unless-stopped
    volumes:
      - ./website:/usr/share/nginx/html:ro
    labels:
      traefik.enable: "true"
      traefik.http.routers.website.rule: Host(`jipwijnia.nl`) || Host(`www.jipwijnia.nl`)
      traefik.http.routers.website.entrypoints: websecure
```

Then point the `@` and `www` records at the VPS.

## Sign-in

Signing in needs an OAuth client registered for this origin: a PR on
`apps/ory-hydra/values.yaml` in FAForever/gitops-stack (public client, PKCE, redirect URIs
`https://vault.jipwijnia.nl/` and `http://127.0.0.1`). The server proxies the token exchange, so no
CORS change at FAF is needed. Then set the new `ClientId` in the viewer's `wwwroot/appsettings.json`;
`appsettings.Production.json` already holds the production redirect URI. See TODO.md.
