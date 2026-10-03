# Deployment — vault.jipwijnia.nl

The hosted viewer runs as a container behind Traefik on a small VPS (TransIP V1: 1 vCPU, 1 GB is
plenty: replays are parsed in the visitor's browser, the server only serves files and proxies the
OAuth token exchange). Visitor statistics come from a self-hosted GoatCounter at
https://stats.jipwijnia.nl. Measured locally: Traefik ~30 MB, the vault ~20 MB, GoatCounter ~45 MB.

| File | What |
|---|---|
| `../Dockerfile` | Image: `FAForever.Replay.Server` with the viewer, on the chiseled ASP.NET runtime (non-root, port 8080). |
| `../.github/workflows/docker.yml` | CI: tests, then builds and pushes `ghcr.io/garanas/scfa-cs-replay:latest` and `:sha-<commit>` on every push to the `deploy/production` branch (not on `main`), and deploys it to the VPS. |
| `deploy.sh` | Pulls the image and restarts the vault; installed as `/usr/local/bin/vault-deploy`, the only command the GitHub deploy key may run. |
| `compose.yaml` | Production stack: Traefik (HTTPS via Let's Encrypt HTTP-01, HTTP → HTTPS), the vault and GoatCounter. |
| `setup.sh` | One-time setup of a fresh Ubuntu VPS (user, SSH keys only, firewall, updates, swap, Docker, the `deploy` user, `/opt/vault`). |
| `compose.local.yaml` | Override to run the same stack locally from source, without Let's Encrypt. |
| `.env.example` | Settings for the server (`ACME_EMAIL`, `VAULT_HOST`, `STATS_HOST`, `VAULT_IMAGE`). |

## Try it locally

```sh
docker compose -f deploy/compose.yaml -f deploy/compose.local.yaml up --build -d
# http://vault.localhost, GoatCounter at http://stats.localhost
docker compose -f deploy/compose.yaml -f deploy/compose.local.yaml down
```

## First-time server setup

1. **Server**: on a fresh Ubuntu VPS, logged in with your SSH key, run
   ```sh
   curl -fsSL https://raw.githubusercontent.com/Garanas/scfa-cs-replay/deploy/production/deploy/setup.sh -o setup.sh
   sudo bash setup.sh jip
   ```
   It creates the user `jip`, allows SSH keys only, opens only ports 22/80/443, enables automatic
   security updates and swap, installs Docker and puts `compose.yaml` and `.env` in `/opt/vault`.
   Keep the session open until `ssh jip@<server>` works in a second terminal.
2. **DNS** at TransIP: `A` records (and `AAAA` for IPv6) for `vault` and `stats` pointing at the VPS.
   Leave the other records of jipwijnia.nl alone (`@`, `www`, `MX`): the website and mail stay where
   they are.
3. **Stack**: fill in `ACME_EMAIL` in `/opt/vault/.env`, then `cd /opt/vault && docker compose up -d`.
   Traefik requests a certificate per host name on its first HTTPS request. The image is public on
   GHCR, so no `docker login` is needed.
4. **GoatCounter**: create the site and your account right away, before anyone else can open the
   setup wizard at https://stats.jipwijnia.nl:
   ```sh
   docker compose exec goatcounter goatcounter db create site -vhost=stats.jipwijnia.nl -user.email=<you>
   ```
   Then log in at https://stats.jipwijnia.nl. Page views are paths (`/replay/123`); which replay tabs
   are used shows up as events (`tab/buildorder`). Visits from `localhost` are never counted.

## Releasing and updating

A new image is only built from the `deploy/production` branch. To release what is on `main`:

```sh
git push origin main:deploy/production     # CI tests, builds, pushes :latest and deploys
```

The `deploy` job connects as the user `deploy` and runs `vault-deploy` (`deploy.sh`): pull the
image, restart the vault. It only ever changes the image. Changes to `compose.yaml` or `.env` are
rolled out by hand, on purpose — a compose file can mount the host, which is not for a CI key:

```sh
cd /opt/vault && docker compose pull && docker compose up -d
```

Roll back by setting `VAULT_IMAGE=ghcr.io/garanas/scfa-cs-replay:sha-<commit>` in `.env` and running
`docker compose up -d`; while that pin is in place, deploys keep pulling the pinned image.

### Setting up the deploy key (once)

1. A key pair just for GitHub, without a passphrase (on your own machine):
   ```sh
   ssh-keygen -t ed25519 -N "" -C github-deploy -f github-deploy
   ```
2. On the server (`setup.sh` created the user `deploy` and `vault-deploy`), restrict the key to
   that one command:
   ```sh
   echo 'command="/usr/local/bin/vault-deploy",restrict <contents of github-deploy.pub>' \
       | sudo tee /home/deploy/.ssh/authorized_keys
   ```
3. GitHub → Settings → Environments → `production` (limit it to the `deploy/production` branch):
   - secret `DEPLOY_SSH_KEY`: the contents of `github-deploy` (the private key);
   - variable `DEPLOY_HOST`: the server's address;
   - variable `DEPLOY_KNOWN_HOSTS`: the output of `ssh-keyscan -t ed25519 <server>`, after checking
     its fingerprint against `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub` on the server.
4. Delete both key files from your machine; GitHub holds the only copy. A new key is a matter of
   repeating these steps.

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
