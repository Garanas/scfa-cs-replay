# Agent guide — FAForever.Replay.Viewer (the UI)

The Blazor WebAssembly app, hosted by `FAForever.Replay.Server`. The repository-wide guide is
[`../AGENTS.md`](../AGENTS.md); the replay model's semantics (entity ids, `ClearQueue`, lobby data,
drawings, game-data tables) are in [`../FAForever.Replay/AGENTS.md`](../FAForever.Replay/AGENTS.md).

## Replay parsing in WebAssembly — hard rules

- **Never call `ReplayLoader.*FromDisk` in the Viewer** — there is no filesystem in the browser.
  Use the staged API: `ReplayLoadingStage.NotStarted` → `ReplayLoader.ProcessReplayStage(...)` →
  … → `Complete`, looping while the stage is `AtInput` and yielding to the browser between batches
  (`await Task.Delay(1)`). `ReplayBodyInvariant.PercentageProcessed` drives progress UI.
- **No `Task.Run` for CPU work in WASM** — it is single-threaded; `Task.Run` buys nothing and breaks
  `StateHasChanged` expectations. Yield cooperatively instead.
- Always handle `ReplayLoadingStage.Failed` and wrap the pump in try/catch: the loader throws on
  unknown input types and malformed Lua data.

## Build, links and analytics

- Tailwind: the standalone CLI lives at `tools/tailwindcss.exe` (gitignored). Install it once with
  `tools/install-tailwind.ps1`. The Viewer's MSBuild target regenerates `wwwroot/css/app.css` on every
  build when the CLI is present; the generated file is **committed** so builds work without it (CI).
- Keep **in-app links base-relative** (`href="replay/123"`, `NavigateTo("search")`, `href=""` for
  home — no leading `/`), so the app also works below a path.
- Analytics: a self-hosted GoatCounter (in jipwijnia-vps, https://stats.jipwijnia.nl), no
  cookies or personal data. `Services/Analytics/AnalyticsService.cs` counts a page view per path
  change (query left out) and an event per replay-tab change (`tab/<name>`), only when
  `Analytics:GoatCounter` is set — which only `wwwroot/appsettings.Production.json` does.

## Conventions

- Styling: Tailwind utilities in markup; recurring patterns become `@layer components` classes in
  `Styles/app.css` (`.card`, `.btn-primary`, `.input-field`, …). No inline
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
- Faction icons live in `wwwroot/images/factions/` (copied from the FAF game
  repo, `textures/ui/common/faction_icon-lg`, `_med` variants); render them via the display helpers
  in `Services/Theming/Factions.cs` (icon path, name, swatch per faction index).
- Army colours: **use `ReplayPlayerOptions.Color`** (CSS hex), see the parser guide.
- Command-type marker icons (Playthrough tab): `wwwroot/images/commands/<slug>.png`, slug = the
  lowercase `CommandCategory` name (`move`, `attack`, `launch`, …), generated from the game's
  waypoint button textures (`textures/ui/common/game/waypoints/*.dds`) by
  `tools/convert-command-icons.ps1` (ImageMagick 7; never edit them by hand).
  `Services/Commands/CommandIcons.cs` holds the manifest of available slugs; a category without a
  PNG (currently only `special`) uses the inline SVG glyph symbols in `PlaythroughMarkerLayer.razor`.
  Adding an icon = extend the script mapping, re-run it, add the slug to `CommandIcons.Available`
  (no onerror fallback on purpose: SVG `<image>` does not fire error events reliably).
- Unit icons live in `wwwroot/images/units/`, generated from the FAF game repo
  (`textures/ui/common/icons/units/*.dds`) by `tools/convert-unit-icons.ps1` (ImageMagick 7). Never edit
  them by hand; re-run the script. See "Unit icon atlas" below.

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
| `view` | Below the maps on the Build order tab: `timings` shows key moments and units ordered per minute, `units` the units (entities) of both players | The order ledger |
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

### Guardrails: every view a moderator could point at is a link

Replay links are evidence: moderators send them to (banned) players to show what a decision is
based on, and they end up in moderation records. Treat them as a public contract.

- **Replay pages work signed out.** The replay download is anonymous and all analysis runs in the
  browser, so no tab, panel or parameter may require a login. Data that needs a token (match
  outcome, ratings) is an optional enrichment that is left out silently when signed out — never a
  sign-in prompt in place of the replay.
- **Sort every piece of state into one of two kinds.** Shareable: it changes *what* is shown — tab,
  filters, time window, playback position, and any selection that points at something (a chat
  message, an order, a unit). It goes in the URL. Transient: hover, open menus, scroll position,
  playback while running, animations. It stays in fields. Rule of thumb: if a moderator would say
  "look at *this*", it is shareable.
- **Selection keys come from the replay, not from the page.** Identify an item by data that is the
  same on every machine and in every release — tick + player name (+ ordinal within that tick),
  entity id, blueprint id — never by a list index, render order or a counter assigned while
  loading. A link must select the same item after the list is filtered differently or the app is
  updated.
- **A link restores the view, not just the state:** a selected item from the URL is highlighted
  *and* scrolled into view on load (`scrollToSelected`), on the map as well as in the list.
- **Names and formats are stable.** Never rename or repurpose a parameter or a value; when one has
  to change, keep reading the old form. Players are referenced by name, times in game time
  (`12:30`), tabs by their query value — not by indices that depend on lobby order.
- **No hidden inputs.** What is shown may depend only on the replay and the URL. Browser storage
  holds personal preferences only (the faction theme), never something that filters or selects.
- **The login round trip keeps the full URL.** `AuthService.BeginLoginAsync` stores the address,
  query included, and the callback returns to it. Any new redirect (another provider, an error
  page) must do the same.
- **Check it before merging** a change to a replay tab: set up a view, copy the address, open it in
  a private window (signed out) and confirm the identical view, selection included. Then add the
  parameter to the table above.

## Unit icon atlas

`tools/convert-unit-icons.ps1` writes into `wwwroot/images/units/`:

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

## Browser automation (Playwright MCP)

`.mcp.json` (repo root) registers the official Playwright MCP server (`npx -y @playwright/mcp@latest`), so agents
can drive the Viewer in a real browser: navigate, snapshot the accessibility tree, click, read console
output and inspect network traffic.

- **Start the app first** — the MCP server only drives a browser, it does not host anything. Run the
  `server` task (or `dotnet watch --project FAForever.Replay.Server`) and navigate to
  `http://127.0.0.1:5080`; the fixed port matters for OAuth (see the root guide).
- Project-scoped MCP servers need a one-off approval per machine. Accept the prompt on the next
  Claude Code start, or run `claude mcp list` to check the connection.
- Screenshots, traces and downloads land in `.playwright-mcp/` at the repo root (gitignored). The viewport defaults to
  1440x900 — the Viewer's layout is desktop-first.
- `.claude/settings.json` pre-approves the navigation, inspection and interaction tools.
  `browser_evaluate`, `browser_run_code_unsafe` and `browser_file_upload` deliberately still prompt.
- The browser profile is temporary: an FAF login does **not** survive a browser restart. For a
  persistent session add `--user-data-dir=.playwright-mcp/profile` to the args — that stores real
  session cookies on disk, so keep the directory gitignored.
- This is an inspection tool, not a test suite. There is no Playwright test project; automated
  coverage lives in `FAForever.Replay.Test` (MSTest).
