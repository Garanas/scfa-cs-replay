# Agent guide: scfa-cs-replay

A .NET 10 solution for parsing and analysing Supreme Commander: Forged Alliance (Forever) replays,
with a Blazor WebAssembly front-end for searching the FAForever vault and inspecting replays.

This file holds what applies everywhere. Each top-level folder and three projects have their own
guide, which agents that support nested guides pick up when they touch files there. Read it before
planning work there:

- **Folders:** [`src/AGENTS.md`](src/AGENTS.md) (dependency rules, library rules, adding a library),
  [`tests/AGENTS.md`](tests/AGENTS.md) (running, assets, the fingerprint test),
  [`benchmarks/AGENTS.md`](benchmarks/AGENTS.md) (running, logging results),
  [`sandbox/AGENTS.md`](sandbox/AGENTS.md) (the scratch CLI).
- **Replay parser work** (`FAForever.FileFormats.Replay`):
  [`src/FAForever.FileFormats.Replay/AGENTS.md`](src/FAForever.FileFormats.Replay/AGENTS.md) covers the
  replay model's semantics (entity ids, `ClearQueue`, lobby data, Lua booleans), game-data tables.
- **Blueprint and Lua work** (`FAForever.FileFormats.Blueprints`, `FAForever.FileFormats.Lua`):
  [`src/FAForever.FileFormats.Blueprints/AGENTS.md`](src/FAForever.FileFormats.Blueprints/AGENTS.md) covers
  the blueprint parser, the Lua evaluator, blueprint ids and unit names.
- **UI work** (`FAForever.Vault.Viewer`): [`src/FAForever.Vault.Viewer/AGENTS.md`](src/FAForever.Vault.Viewer/AGENTS.md)
  covers WebAssembly rules, styling and theming, icons, shareable view state (the URL guardrails),
  the Moderation tab and its AI prompt, analytics, Playwright.

## Solution layout

Folders by role (`src/`, `tests/`, `benchmarks/`, `sandbox/`); the solution groups the projects by
product in the solution folders `FileFormats` and `Vault`.

| Project | Purpose |
|---|---|
| `src/FAForever.FileFormats.Lua` | Lua values (`LuaData`), their formatter, and an evaluator for the data-only Lua that game files are written in. |
| `src/FAForever.FileFormats.Blueprints` | Blueprint files (`.bp`), blueprint ids, unit names and factions. References Lua. |
| `src/FAForever.FileFormats.Replay` | Core replay parser (the crown jewel: change with care, it is benchmarked and heavily tested). References Lua and Blueprints. |
| `src/FAForever.Vault.Viewer` | Standalone Blazor WebAssembly app (UI). Tailwind CSS v4, no component library. |
| `src/FAForever.Vault.Server` | Minimal ASP.NET Core host: serves the Viewer's static files, proxies the OAuth token exchange **and** fills in link previews for replay, unit and About pages. |
| `tests/FAForever.FileFormats.*.Tests` | MSTest suites, one per library, with real assets under `assets/`. |
| `benchmarks/FAForever.FileFormats.Replay.Benchmarks` | BenchmarkDotNet harness. |
| `sandbox/FAForever.FileFormats.Replay.Sandbox` | CLI scratch pad. |

Shared MSBuild settings: `Directory.Build.props` at the root (target framework, nullable, implicit
usings) for every project; `tests/Directory.Build.props` adds the MSTest packages and
`tests/Directory.Build.targets` copies each test project's `assets/` to the output. A new test
project only lists its project reference.

## Commands

```sh
dotnet build FAForever.sln                              # build everything
dotnet test FAForever.sln                               # run every test project
dotnet watch --project src/FAForever.Vault.Server       # run the hosted app on http://127.0.0.1:5080
tools/tailwindcss.exe -i Styles/app.css -o wwwroot/css/app.css --watch   # from src/FAForever.Vault.Viewer/
```

VS Code: tasks `build`, `test`, `test: watch`, `server`, `viewer`, `tailwind: watch`, `benchmark`.

- The server **must** run on `http://127.0.0.1:5080` in development: the development OAuth client only
  accepts loopback redirect URIs, and `wwwroot/appsettings.json` pins `RedirectUri` to that origin.
- Deployment: a container image (`Dockerfile`: the Server with the Viewer) is built and pushed to
  `ghcr.io/garanas/scfa-cs-replay` on every push to the `deploy/production` branch; releasing is
  `git push origin main:deploy/production`; pushes to `main` build no image
  (`.github/workflows/docker.yml`, after the tests); its `deploy` job then has the VPS pull the image
  over SSH (environment `production`). It runs behind Traefik at https://vault.jipwijnia.nl.
  Everything about the server (compose stack, setup script, deploy key, runbook) lives in a
  separate public repository, [Garanas/jipwijnia-vps](https://github.com/Garanas/jipwijnia-vps); try
  the image locally with `docker build -t scfa-cs-replay . && docker run --rm -p 8080:8080 scfa-cs-replay`.
  The footer shows the commit a build came from (`src/FAForever.Vault.Viewer/Services/BuildInfo.cs`; CI
  passes it as the `SOURCE_REVISION` build argument, since the image build has no `.git`).

## External FAForever endpoints (verified 2026-10)

| Endpoint | Auth | CORS | Notes |
|---|---|---|---|
| `https://hydra.faforever.com/oauth2/auth` | — | n/a (redirect) | OAuth2 authorization endpoint (Ory Hydra), PKCE S256. |
| `https://hydra.faforever.com/oauth2/token` | PKCE | **none** | Browsers cannot call it; the Server proxies it at `/api/oauth/token`. |
| `https://api.faforever.com/data/*` | Bearer token required (401 otherwise) | `*` | JSON:API (Elide) with RSQL filters; call directly from the browser. |
| `https://api.faforever.com/me` | Bearer token | `*` | Current user. |
| `https://api.faforever.com/game/{id}/replay` | anonymous | `*` | 302 → `content.faforever.com/replays/...fafreplay`; browser fetch can follow it. Do **not** proxy replay downloads. |
| `https://content.faforever.com/maps/previews/small/{map}.png` | anonymous | n/a for `<img>` | Map preview images (`large/` too, used for link previews). |
| `https://mapgen.services.atlantishq.de/api-dev/request/preview/{map}` | anonymous | **none** (fine for `<img>`) | Previews of generated maps (`neroxis_map_generator_{version}_{seed}_{options}`), 256 px PNG of the whole map, transparent outside the playable area. Rendered on request (a new map takes seconds); only generator versions 1.19.0, 1.21.1 and 1.21.2, a 500 for anything else (verified 2026-10-05). |

OAuth clients (both public, PKCE):
- Production: our own client "Web vault by Jip Wijnia" (`54576b8e-14bc-473d-9f85-31e6327c9e3b`,
  registered in [FAForever/gitops-stack#333](https://github.com/FAForever/gitops-stack/pull/333),
  `apps/ory-hydra/values.yaml`), set in `wwwroot/appsettings.Production.json`. Its only redirect URI
  is `https://vault.jipwijnia.nl/`; it does not accept loopback.
- Development: the official FAF desktop client (`2e8808cf-5889-469b-b2c3-01f0cc58c4af`, in
  `wwwroot/appsettings.json`), which accepts loopback redirects without a path, hence the fixed dev
  port and a redirect URI of exactly `http://127.0.0.1:5080`.

## Conventions

- C#: nullable enabled, implicit usings, records for data, `PascalCase` members. Match the existing
  style of the file you touch; the core library favours small immutable records and pure static
  functions (`ReplaySemantics`, `ReplayAnalysis`).
- NuGet versions live **only** in `Directory.Packages.props` (central package management).
- UI text is English. Code identifiers are English.
- Write text (UI, comments, docs) for a reader who never saw an earlier version. When a correction
  rewrites it, state what is true; do not contrast with the wording it replaces ("not X", "no
  longer", "instead of the old"), unless the reader would otherwise assume X. History belongs in
  the commit message.
- Tests: MSTest with `[DataRow]` over the real assets in each test project's `assets/` (replays in `tests/FAForever.FileFormats.Replay.Tests/assets/`).
- Keep the Server minimal: static hosting, the token proxy and link previews. It must never hold
  secrets or session state.
- Link previews (`src/FAForever.Vault.Server/ReplayLinkPreview.cs`): unfurlers (Discord, X, Slack) do not
  run the app, so `/replays/{id}` is served as `index.html` with that replay's Open Graph tags in place
  of the default block between `<!-- Link preview` and `<!-- /Link preview -->`. The data is the
  replay file's first line (its JSON metadata), fetched anonymously with a Range request; the replay
  itself is never downloaded or passed on. Cards are cached in memory (a day; failures five minutes)
  and uncached lookups are rate-limited per client address. Map names come from the vault folder
  (`osiris.v0006` becomes "Osiris"); the original maps (`scmp_009`, `x1mp_017`) have only a code, so
  their card shows no name.
- Link previews of the other pages (`PageLinkPreview.cs`, same block, helpers in `LinkPreviewHtml.cs`):
  the unit pages get a card about the units in their address (`units/database?unit=`, `compare=`,
  `version=`, `units/history?units=`, the old `/units?unit=`), read from the app's own unit data with
  FAForever.FileFormats.Blueprints, with the unit's icon as a small image; a search with criteria gets
  a card about what it searched for (`player`, `map`, `mod`, `around`, `within`, `finished`; the
  results need a login, so never what was found); the home page, the replay search (`/replays`), the map pages (`/maps`, `/maps/featured`, `/maps/ladder`, `/maps/search`), the local replay page, the
  About and Units pages have a fixed card each (`PageLinkPreview.Paths`, mapped in `Program.cs`). A
  card without a unit shows the app icon (`icons/icon-512.png`). Nothing is fetched, so no rate
  limit; unit cards are cached for ten minutes. A new page gets a card in `FixedCards`; a new query
  parameter that changes what a unit page or the search shows belongs in its card too.

## Gotchas

- Player names come from `Replay.Header.Clients[input.SourceId]`; ticks are **10 per second**.
- `ReplayLoadingStage` is declared in the **global namespace** (not `FAForever.FileFormats.Replay`).
- `ReplayMetadata` field names are mixed-case on purpose (they mirror the JSON): `uid`, `mapname`,
  `launched_at` (unix seconds), `num_players`, `FeaturedMod`, …
- The test workflow (`.github/workflows/test.yml`) runs on Linux: anything Windows-only
  (e.g. `tools/tailwindcss.exe`) must stay optional in the build.
- `TODO.md` at the repo root tracks open items that need the project owner (OAuth client
  registration, live-login verification, …). Add to it when blocked instead of guessing.
