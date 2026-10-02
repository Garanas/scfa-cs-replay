# Agent guide — scfa-cs-replay

A .NET 10 solution for parsing and analysing Supreme Commander: Forged Alliance (Forever) replays,
with a Blazor WebAssembly front-end for searching the FAForever vault and inspecting replays.

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
- Tailwind: the standalone CLI lives at `tools/tailwindcss.exe` (gitignored). Install it once with
  `tools/install-tailwind.ps1`. The Viewer's MSBuild target regenerates `wwwroot/css/app.css` on every
  build when the CLI is present; the generated file is **committed** so builds work without it (CI).

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

## Replay parsing in WebAssembly — hard rules

- **Never call `ReplayLoader.*FromDisk` in the Viewer** — there is no filesystem in the browser.
  Use the staged API: `ReplayLoadingStage.NotStarted` → `ReplayLoader.ProcessReplayStage(...)` →
  … → `Complete`, looping while the stage is `AtInput` and yielding to the browser between batches
  (`await Task.Delay(1)`). `ReplayBodyInvariant.PercentageProcessed` drives progress UI.
- **No `Task.Run` for CPU work in WASM** — it is single-threaded; `Task.Run` buys nothing and breaks
  `StateHasChanged` expectations. Yield cooperatively instead.
- Always handle `ReplayLoadingStage.Failed` and wrap the pump in try/catch: the loader throws on
  unknown input types and malformed Lua data.
- `.fafreplay` = JSON metadata line + compressed body (zstd, or legacy base64+zlib);
  `.scfareplay` = raw body — construct `ReplayLoadingStage.Decompressed(stream, null)` directly.
- `ProcessReplayStage(WithMetadata)` disposes the input stream; don't reuse it.
- Player names come from `Replay.Header.Clients[input.SourceId]`; ticks are **10 per second**.
- Lobby data (factions, teams, ratings) is **not** in the parsed header (`ArmyOptions` is always
  empty); fetch it from the FAF API instead.

## Conventions

- C#: nullable enabled, implicit usings, records for data, `PascalCase` members. Match the existing
  style of the file you touch; the core library favours small immutable records and pure static
  functions (`ReplaySemantics`, `ReplayAnalysis`).
- NuGet versions live **only** in `Directory.Packages.props` (central package management).
- UI text is English. Code identifiers are English.
- Styling: Tailwind utilities in markup; recurring patterns become `@layer components` classes in
  `FAForever.Replay.Viewer/Styles/app.css` (`.card`, `.btn-primary`, `.input-field`, …). No inline
  `<style>` blocks in components; no component library.
- Theming: four faction themes (UEF default, Cybran, Aeon, Seraphim) implemented as `--th-*` CSS
  custom properties switched by `data-theme` on `<html>`, mapped to Tailwind tokens via
  `@theme inline`. **Always use the semantic utilities** (`bg-surface`, `text-ink-muted`,
  `border-edge`, `bg-primary`, …) — never hardcode colours in components, or faction switching breaks.
- Tests: MSTest with `[DataRow]` over the real replay assets in `FAForever.Replay.Test/assets/`.
- Keep the Server minimal: static hosting + token proxy. It must never hold secrets or session state.

## Gotchas

- `ReplayLoadingStage` is declared in the **global namespace** (not `FAForever.Replay`).
- `ReplayMetadata` field names are mixed-case on purpose (they mirror the JSON): `uid`, `mapname`,
  `launched_at` (unix seconds), `num_players`, `FeaturedMod`, …
- The test workflow (`.github/workflows/test.yml`) runs on Linux: anything Windows-only
  (e.g. `tools/tailwindcss.exe`) must stay optional in the build.
- `TODO.md` at the repo root tracks open items that need the project owner (OAuth client
  registration, live-login verification, …). Add to it when blocked instead of guessing.
