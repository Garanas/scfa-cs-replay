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
- Benchmarks (`FAForever.Replay.Benchmark`, always `-c Release`): without arguments everything runs;
  `dotnet run -c Release -- --filter "*ParseBenchmark.Body*"` runs one phase. Phases: `DecompressBenchmark`
  (metadata + decompression), `ParseBenchmark.Header` / `.Body` (on pre-decompressed bytes, body via the
  staged API with the Viewer's batch size), and the end-to-end `FAForeverReplayBenchmark` /
  `SCFAReplayBenchmark`. Log results per optimisation step in `FAForever.Replay.Benchmark/RESULTS.md`.
- Parser changes must keep `ReplayFingerprintTest` green: it pins a hash of the complete parsed output
  of every test asset, and checks that the staged API matches a one-shot load. Only update the expected
  hashes when an output change is intended.

## Browser automation (Playwright MCP)

`.mcp.json` registers the official Playwright MCP server (`npx -y @playwright/mcp@latest`), so agents
can drive the Viewer in a real browser: navigate, snapshot the accessibility tree, click, read console
output and inspect network traffic.

- **Start the app first** — the MCP server only drives a browser, it does not host anything. Run the
  `server` task (or `dotnet watch --project FAForever.Replay.Server`) and navigate to
  `http://127.0.0.1:5080`; the fixed port matters for OAuth (see above).
- Project-scoped MCP servers need a one-off approval per machine. Accept the prompt on the next
  Claude Code start, or run `claude mcp list` to check the connection.
- Screenshots, traces and downloads land in `.playwright-mcp/` (gitignored). The viewport defaults to
  1440x900 — the Viewer's layout is desktop-first.
- `.claude/settings.json` pre-approves the navigation, inspection and interaction tools.
  `browser_evaluate`, `browser_run_code_unsafe` and `browser_file_upload` deliberately still prompt.
- The browser profile is temporary: an FAF login does **not** survive a browser restart. For a
  persistent session add `--user-data-dir=.playwright-mcp/profile` to the args — that stores real
  session cookies on disk, so keep the directory gitignored.
- This is an inspection tool, not a test suite. There is no Playwright test project; automated
  coverage lives in `FAForever.Replay.Test` (MSTest).

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
- Command selections (`CommandUnits.EntityIds`) carry the entity ids the order went to. An entity id
  is `(army index << 20) | serial`; serial 0 is the army's commander (ACU), later serials follow the
  order in which units appear. What kind of unit an id is, the replay does not say;
  `ReplaySemantics.GetEntities` guesses it from the orders (`ReplayEntityKind`).
- `CommandData.ClearQueue` is the last byte of a command: 1 = the order replaces the queue, 0 = it was
  shift-queued. (It used to be called `AddToQueue`, the inverse; verified on real replays, see its doc.)
- Drawings ("painting", `lua/ui/game/painting`) are the `SharePaintingBrushStroke` sim callback:
  `{ShareablePainting={PeerName, ShareId, PaintingAdapterIdentifier, Samples={x,y,z,x,y,z,…}}}`, one
  callback per stroke, sent only by its author (no dedup needed, unlike chat). Observers share
  paintings through chat, so theirs are not in the replay. Use `ReplaySemantics.GetDrawings`;
  pings come from `ReplaySemantics.GetPings`. Both are shown on the Chat tab (`ChatPanel` → `ChatMapLayer`/`ChatFeed`) and, alive for their in-game lifetime, on the Playthrough map (same `ChatMapLayer`).
- Lobby data lives in `Replay.Header.Armies` (`ReplayPlayerOptions`: faction 1=UEF/2=Aeon/
  3=Cybran/4=Seraphim, team where 1 = FFA, start spot, colors, rating MEAN/DEV, country, clan;
  `Raw` holds the full Lua table, e.g. `OwnerID`). `Armies[i].SourceId` links an army to
  `Header.Clients`; it is null for AI and civilian armies. Clients without an army are observers.
  Game options live in `Header.Scenario.Options`. What a replay does **not** know is who won —
  that comes from the FAF API only.
- Lua booleans on the wire are `0 = false`; lobby options additionally encode booleans as the
  strings `'true'/'false'`, `'On'/'Off'` or `'Yes'/'No'` (see the `GetFlexibleBool` reader in
  `ReplayLoader`). Beware: faf-java-commons reads Lua booleans inverted (`== 0`); we verified
  against real replays (known humans must have `Human == true`) and deliberately diverge.

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
- Page width: header, page and footer share `MainLayout.Container` (`max-w-6xl`). The Build order
  tab compares two players side by side and widens it to a full HD screen (`max-w-[1824px]`).
- Anything drawn on the map goes through `Features/Replay/MapCanvas.razor`: an SVG in world
  coordinates with the vault preview as backdrop, so replay positions (x, z) are used as-is; its
  `ViewBox` parameter zooms in on a part of the map.
- Faction icons live in `FAForever.Replay.Viewer/wwwroot/images/factions/` (copied from the FAF game
  repo, `textures/ui/common/faction_icon-lg`, `_med` variants); render them via the display helpers
  in `Services/Theming/Factions.cs` (icon path, name, swatch per faction index).
- Game-data tables mirrored from the FA repo, with their lua sources: in-game army colours in
  `FAForever.Replay/GameColors.cs` ← `lua/GameColors.lua`. The parsed model resolves them:
  **use `ReplayPlayerOptions.Color`** (CSS hex) rather than the raw `PlayerColor`/`ArmyColor`
  indices; `GameColors.BySource(header)`/`ByName(header)` give lookup maps for inputs and chat.
  Lobby option keys/values ← `lua/ui/lobby/lobbyOptions.lua` (see `ReplayScenarioOptions` and the
  `GetFlexibleBool` reader). Unit display names ← the `Description` fields of `units/*_unit.bp`,
  generated into `FAForever.Replay/UnitNames.g.cs` by `tools/generate-unit-names.ps1`
  (`UnitNames.GetOrNull(blueprintId)`; never edit the generated file). Re-check all of these
  after game updates.
- Command-type marker icons (Playthrough tab): `wwwroot/images/commands/<slug>.png`, slug = the
  lowercase `CommandCategory` name (`move`, `attack`, `launch`, …), generated from the game's
  waypoint button textures (`textures/ui/common/game/waypoints/*.dds`) by
  `tools/convert-command-icons.ps1` (ImageMagick 7; never edit them by hand).
  `Services/Commands/CommandIcons.cs` holds the manifest of available slugs; a category without a
  PNG (currently only `special`) uses the inline SVG glyph symbols in `PlaythroughMarkerLayer.razor`.
  Adding an icon = extend the script mapping, re-run it, add the slug to `CommandIcons.Available`
  (no onerror fallback on purpose: SVG `<image>` does not fire error events reliably).
- Unit icons live in `FAForever.Replay.Viewer/wwwroot/images/units/`, generated from the FAF game repo
  (`textures/ui/common/icons/units/*.dds`) by `tools/convert-unit-icons.ps1` (ImageMagick 7). Never edit
  them by hand; re-run the script. See "Unit icon atlas" below.
- Tests: MSTest with `[DataRow]` over the real replay assets in `FAForever.Replay.Test/assets/`.
- Keep the Server minimal: static hosting + token proxy. It must never hold secrets or session state.

## Shareable view state (query parameters)

**The URL is the single source of truth for view state**, so any view of a replay can be shared by
copying the address: `/replay/23225104?tab=chat&players=MarcusM,Printer` opens the Chat tab filtered
to those two players, on any machine.

| Parameter | Meaning | Default when absent |
|---|---|---|
| `tab` | Active replay section: `players`, `playthrough`, `buildorder`, `chat`, `events`, `callbacks`, `analysis` | Overview |
| `players` | Comma-separated player names to show; shared by Playthrough, Chat, Events, Callbacks and Analysis | All players |
| `from` / `to` | Game-time window (`12`, `12:30` or `1:02:30`); shared across tabs | See window policy below |
| `types` | Comma-separated input types shown in the Events stream | All types |
| `endpoint` | Selected sim-callback endpoint | The most frequent endpoint |
| `pings` | `off` hides pings on the Chat tab (map and feed) | Pings shown |
| `compare` | The two players of the Build order tab, `Left,Right` (unknown names fall back per slot) | The first army vs the first army of another team |
| `builds` | `only` shows construction orders only on the Build order tab | All orders |
| `view` | `units` shows the units (entities) of both players instead of the order ledger on the Build order tab | The ledger |
| `combat` | `show` also lists combat units in the units view of the Build order tab | Hidden |
| `at` | Playback position of the Playthrough tab (`12`, `12:30` or `1:02:30`); written on pause/seek only, never while playing | `0:00`, paused |

**Window policy** (`TimeWindowFilter.ReadWindow`): one rule on every tab — a window is at most
**four minutes** (`TimeWindowFilter.MaxWindow`), self-correcting with no error states: reversed
bounds swap, a single bound implies the other, To is pulled along when the window is too long.
Events *requires* a window (default `0:00`–`4:00`, kept out of the URL); everywhere else it is
optional (both parameters absent = whole game). Build order ignores the window: it always covers
the first ten minutes. The stepper buttons are always visible and shift
by the window length, shown as their label (−4:00 / +4:00); without an active window the forward
stepper starts one at `0:00`–`4:00` (the back stepper is disabled until there is one).
The **Playthrough tab is exempt** from the window policy: it is a playback view, not a filtered
list — its shareable state is the single `at` instant and it deliberately has no `from`/`to`.
Playback state (playing, speed, current time while playing) lives in component fields; `at` is
written only on pause or seek-while-paused, so playing never floods the URL or re-renders siblings.
Reuse `Features/Replay/TimeWindowFilter.razor` and `PlayerFilterList.razor` for any new filter panel;
both own their query parameters, and parents re-derive state from the URL in `OnParametersSet`.

**Every component that derives state from the URL must inherit `UrlStateComponent`**
(`Services/UrlStateComponent.cs`, which also provides the protected `Navigation` property — don't
`@inject NavigationManager` on top of it). Gotcha it exists for: a query-only navigation does not
re-render a page whose parameters are unchanged value types — Blazor skips the whole subtree — so a
filter written by one component would never reach its siblings. The base subscribes to
`LocationChanged` and re-runs `OnParametersSet` + render.

The pattern, for any new panel with a selection worth sharing:

- **Read** with `UrlQuery.Get(Navigation, "name")` (`Services/UrlQuery.cs`) inside `OnParametersSet`,
  and derive the full panel state from Model + URL there — every render is then idempotent, and a
  pasted link restores the exact view without extra plumbing.
- **Write** with `Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("name", value), replace: true)`.
  Always `replace: true`: selections must not pollute the browser history.
- **Defaults stay out of the URL**: pass `null` as the value to remove the parameter when the
  selection equals the default (Overview tab, all players, top endpoint). Links stay short and the
  bare URL keeps working.
- **Degrade gracefully**: unknown player names are ignored, an unknown endpoint falls back to the
  default — a stale link to a different replay must never break the page.
- Parameters are independent and may be combined; switching tabs leaves the other parameters alone.

## Unit icon atlas

`tools/convert-unit-icons.ps1` writes into `FAForever.Replay.Viewer/wwwroot/images/units/`:

| File | Contents |
|---|---|
| `units-atlas.png` + `units-atlas.json` | All ~600 unit icons on a grid of 64 px cells (25 columns). |
| `backgrounds-atlas.png` + `backgrounds-atlas.json` | The 13 layer backgrounds in one row. |
| `units/<id>.png`, `backgrounds/<name>.png` | The same images as individual files. |

The JSON index gives the top-left pixel of each cell:
`{"cellSize":64,"columns":25,"rows":24,"icons":{"uel0101":{"x":0,"y":0},…}}`.

- **Use the `<UnitIcon BlueprintId="uel0101" Size="32" />` component** (`Features/Replay/UnitIcon.razor`)
  wherever a unit is shown; it stacks the layer background and the unit icon from the atlases. One
  request instead of hundreds. The scoped `UnitIconAtlas` service (`Services/Units/`) fetches both JSON
  indexes once and shares them across all icons on the page.
- How it renders: `.unit-icon` / `.unit-icon-layer` / `.unit-icon-unit` (`@layer components` in
  `Styles/app.css`) carry the atlas image; the computed `background-size` and `background-position`
  are the one inline `style` that is acceptable. Scaling multiplies everything by `s = Size / cellSize`:
  `background-size` = atlas width/height (`columns`/`rows` × `cellSize`) × `s`, position `-x·s -y·s`.
- **Keys are lowercase blueprint ids** without the `_icon` suffix (`uel0101`, `xsl0401`). Normalise
  ids from replays with `ToLowerInvariant()`; fall back to `default` (a "Place Holder" icon) when an id
  is missing from the index — mods and campaign units often have no icon.
- **Backgrounds:** the game draws a layer background behind each icon, from the blueprint's
  `General.Icon` (`land`, `air`, `sea`, `amph`) plus a state: `_up` (normal), `_over` (hover),
  `_down` (pressed/selected) — `UnitIcon`'s `State` parameter. Replays carry no blueprints, so
  `UnitIconAtlas.Layer` derives the layer from the id's third letter (`a` air, `s` sea, else land);
  amphibious units therefore show the land background.
  `cons_bar` is the construction progress overlay, not a layer.
- Icons are transparent PNGs; a few source icons are 32 or 72 px and sit centred (72 px ones scaled
  down) in their 64 px cell. The individual files keep their original size.
- Use the individual files only for one-offs (e.g. a single `<img>` on a detail page).
- After an FA game update, re-run `pwsh tools/convert-unit-icons.ps1 -Source <fa repo>/textures/ui/common/icons/units`
  and commit the result; output is deterministic (PNG timestamps are stripped), so the diff only
  shows real changes. Cell positions can shift when icons are added, so never hardcode coordinates.

## Gotchas

- `ReplayLoadingStage` is declared in the **global namespace** (not `FAForever.Replay`).
- `ReplayMetadata` field names are mixed-case on purpose (they mirror the JSON): `uid`, `mapname`,
  `launched_at` (unix seconds), `num_players`, `FeaturedMod`, …
- The test workflow (`.github/workflows/test.yml`) runs on Linux: anything Windows-only
  (e.g. `tools/tailwindcss.exe`) must stay optional in the build.
- `TODO.md` at the repo root tracks open items that need the project owner (OAuth client
  registration, live-login verification, …). Add to it when blocked instead of guessing.
