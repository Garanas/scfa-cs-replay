# Agent guide — scfa-cs-replay

A .NET 10 solution for parsing and analysing Supreme Commander: Forged Alliance (Forever) replays,
with a Blazor WebAssembly front-end for searching the FAForever vault and inspecting replays.

This file holds what applies everywhere. Two projects have their own guide, which agents that
support nested guides pick up when they touch files there — read it before planning work in that
project:

- **Parser work** (`FAForever.Replay`, its tests and benchmarks): [`FAForever.Replay/AGENTS.md`](FAForever.Replay/AGENTS.md)
  — fingerprint test, benchmarks, the replay model's semantics (entity ids, `ClearQueue`, lobby
  data, Lua booleans), game-data tables.
- **UI work** (`FAForever.Replay.Viewer`): [`FAForever.Replay.Viewer/AGENTS.md`](FAForever.Replay.Viewer/AGENTS.md)
  — WebAssembly rules, styling and theming, icons, shareable view state (the URL guardrails),
  analytics, Playwright.

## Solution layout

| Project | Purpose |
|---|---|
| `FAForever.Replay` | Core replay parser (the crown jewel — change with care, it is benchmarked and heavily tested). |
| `FAForever.Replay.Viewer` | Standalone Blazor WebAssembly app (UI). Tailwind CSS v4, no component library. |
| `FAForever.Replay.Server` | Minimal ASP.NET Core host: serves the Viewer's static files **and** proxies the OAuth token exchange. |
| `FAForever.Replay.Test` | MSTest suite with real replay assets under `assets/`. |
| `FAForever.Replay.Sandbox` | CLI scratch pad. |
| `FAForever.Replay.Benchmark` | BenchmarkDotNet harness. |

## Commands

```sh
dotnet build FAForever.sln                         # build everything
dotnet test FAForever.Replay.Test                  # run the test suite
dotnet watch --project FAForever.Replay.Server     # run the hosted app on http://127.0.0.1:5080
tools/tailwindcss.exe -i Styles/app.css -o wwwroot/css/app.css --watch   # from FAForever.Replay.Viewer/
```

VS Code: tasks `build`, `test`, `test: watch`, `server`, `viewer`, `tailwind: watch`, `benchmark`.

- The server **must** run on `http://127.0.0.1:5080` in development: the temporary OAuth client only
  accepts loopback redirect URIs, and `wwwroot/appsettings.json` pins `RedirectUri` to that origin.
- Deployment: a container image (`Dockerfile`: the Server with the Viewer) is built and pushed to
  `ghcr.io/garanas/scfa-cs-replay` on every push to the `deploy/production` branch — releasing is
  `git push origin main:deploy/production`; pushes to `main` build no image
  (`.github/workflows/docker.yml`, after the tests); its `deploy` job then has the VPS pull the image
  over SSH (environment `production`). It runs behind Traefik at https://vault.jipwijnia.nl.
  Everything about the server — compose stack, setup script, deploy key, runbook — lives in a
  separate public repository, [Garanas/jipwijnia-vps](https://github.com/Garanas/jipwijnia-vps); try
  the image locally with `docker build -t scfa-cs-replay . && docker run --rm -p 8080:8080 scfa-cs-replay`.
  The footer shows the commit a build came from (`FAForever.Replay.Viewer/Services/BuildInfo.cs`; CI
  passes it as the `SOURCE_REVISION` build argument, since the image build has no `.git`).

## External FAForever endpoints (verified 2026-10)

| Endpoint | Auth | CORS | Notes |
|---|---|---|---|
| `https://hydra.faforever.com/oauth2/auth` | — | n/a (redirect) | OAuth2 authorization endpoint (Ory Hydra), PKCE S256. |
| `https://hydra.faforever.com/oauth2/token` | PKCE | **none** | Browsers cannot call it; the Server proxies it at `/api/oauth/token`. |
| `https://api.faforever.com/data/*` | Bearer token required (401 otherwise) | `*` | JSON:API (Elide) with RSQL filters; call directly from the browser. |
| `https://api.faforever.com/me` | Bearer token | `*` | Current user. |
| `https://api.faforever.com/game/{id}/replay` | anonymous | `*` | 302 → `content.faforever.com/replays/...fafreplay`; browser fetch can follow it. Do **not** proxy replay downloads. |
| `https://content.faforever.com/maps/previews/small/{map}.png` | anonymous | n/a for `<img>` | Map preview images. |

OAuth client: we temporarily reuse the official FAF client's **public** client
(`2e8808cf-5889-469b-b2c3-01f0cc58c4af`, PKCE, loopback redirect without a path — hence the fixed dev
port and a redirect URI of exactly `http://127.0.0.1:5080`). A dedicated client must be requested from
the FAF team before any public deployment (see TODO.md).

## Conventions

- C#: nullable enabled, implicit usings, records for data, `PascalCase` members. Match the existing
  style of the file you touch; the core library favours small immutable records and pure static
  functions (`ReplaySemantics`, `ReplayAnalysis`).
- NuGet versions live **only** in `Directory.Packages.props` (central package management).
- UI text is English. Code identifiers are English.
- Tests: MSTest with `[DataRow]` over the real replay assets in `FAForever.Replay.Test/assets/`.
- Keep the Server minimal: static hosting + token proxy. It must never hold secrets or session state.

## Gotchas

- Player names come from `Replay.Header.Clients[input.SourceId]`; ticks are **10 per second**.
- `ReplayLoadingStage` is declared in the **global namespace** (not `FAForever.Replay`).
- `ReplayMetadata` field names are mixed-case on purpose (they mirror the JSON): `uid`, `mapname`,
  `launched_at` (unix seconds), `num_players`, `FeaturedMod`, …
- The test workflow (`.github/workflows/test.yml`) runs on Linux: anything Windows-only
  (e.g. `tools/tailwindcss.exe`) must stay optional in the build.
- `TODO.md` at the repo root tracks open items that need the project owner (OAuth client
  registration, live-login verification, …). Add to it when blocked instead of guessing.
