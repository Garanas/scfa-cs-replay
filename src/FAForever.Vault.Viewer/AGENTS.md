# Agent guide: FAForever.Vault.Viewer (the UI)

The Blazor WebAssembly app, hosted by `FAForever.Vault.Server`. The repository-wide guide is
[`../../AGENTS.md`](../../AGENTS.md); the replay model's semantics (entity ids, `ClearQueue`, lobby data,
drawings, game-data tables) are in [`../FAForever.FileFormats.Replay/AGENTS.md`](../FAForever.FileFormats.Replay/AGENTS.md).

## Replay parsing in WebAssembly: hard rules

- **Never call `ReplayLoader.*FromDisk` in the Viewer**: there is no filesystem in the browser.
  Use the staged API: `ReplayLoadingStage.NotStarted` → `ReplayLoader.ProcessReplayStage(...)` →
  … → `Complete`, looping while the stage is `AtInput` and yielding to the browser between batches
  (`await Task.Delay(1)`). `ReplayBodyInvariant.PercentageProcessed` drives progress UI.
- **No `Task.Run` for CPU work in WASM**: it is single-threaded; `Task.Run` buys nothing and breaks
  `StateHasChanged` expectations. Yield cooperatively instead.
- Always handle `ReplayLoadingStage.Failed` and wrap the pump in try/catch: the loader throws on
  unknown input types and malformed Lua data.

## Build, links and analytics

- Tailwind: the standalone CLI lives at `tools/tailwindcss.exe` (gitignored). Install it once with
  `tools/install-tailwind.ps1`. The Viewer's MSBuild target regenerates `wwwroot/css/app.css` on every
  build when the CLI is present; the generated file is **committed** so builds work without it (CI).
- Keep **in-app links base-relative** (`href="replays/123"`, `NavigateTo("replays")`, `href=""` for
  home, no leading `/`), so the app also works below a path.
- Analytics: a self-hosted GoatCounter (in jipwijnia-vps, https://stats.jipwijnia.nl), no
  cookies or personal data. `Services/Analytics/AnalyticsService.cs` counts a page view per path
  change (query left out) and an event per replay-tab change (`tab/<name>`), only when
  `Analytics:GoatCounter` is set, which only `wwwroot/appsettings.Production.json` does.

## Progressive web app

The Viewer is installable (`wwwroot/manifest.webmanifest`) and starts offline.

- **Service worker:** `wwwroot/service-worker.js` in development (caches nothing, so every build is
  picked up), `wwwroot/service-worker.published.js` when published (`ServiceWorker` item in the
  .csproj). It caches the app shell from the generated `service-worker-assets.js` (published files
  with hashes) and serves `index.html` for every navigation. Data is never cached: vault replays and
  the API always come from the network. The individual unit/background icons are excluded (the app
  uses the atlases); exclude any new bulk folder in `offlineAssetsExclude` too.
- **Updates wait for the user:** a new version installs in the background and
  `Layout/UpdateBanner.razor` offers a reload; the worker only takes over on `skipWaiting` from the
  page, so a local replay held in memory is never reloaded away. The Server sends `no-cache` for
  `index.html`, the service worker, its asset list and the manifest.
- **File handling:** the installed app is registered for `.fafreplay`/`.scfareplay` ("Open with").
  `js/app.js` takes the file from the `launchQueue` (which may deliver before Blazor runs) and
  `Pages/Home.razor` loads it like a picked file (`Services/Replays/LocalReplayLoader.cs`, the one way every local file is opened). `launch_handler` (`navigate-existing`) opens it in
  the app window that is already open instead of a new one (Chromium; others ignore it).
- **Share target:** the installed app takes shared links (`share_target`, GET to `./` with
  `title`, `text`, `url`). `Pages/Home.razor` opens a link into the app as is, a
  `replay.faforever.com` link or a bare id as that replay, and puts anything else in the replay input.
- **Share buttons:** the installed app has no address bar, so a page with shareable view state gets
  a `Layout/ShareButton.razor` (the system's share sheet where there is one, else a copy of the
  address): the vault replay page, the unit database (in the heading, or in the details panel while
  a unit is selected, since the panel covers the heading on phones) and the unit history. A new page
  with state in its URL gets one too, with a link title that names what it shows.
- **Shortcuts:** the manifest's `shortcuts` (Search, About) appear when right-clicking or
  long-pressing the installed app's icon. Keep their URLs relative, like the in-app links.
- **Icons:** `wwwroot/icons/icon.svg` is the source; `tools/convert-app-icons.ps1` (ImageMagick 7)
  renders the favicon, manifest and Apple touch icons. Never edit the PNGs by hand.
- `theme-color` follows the light/dark mode (`fafReplay.syncThemeColor` in `applyMode`, from `--th-base`).
- Test PWA behaviour on a **published** build (`dotnet publish src/FAForever.Vault.Server -c Release`),
  served from 127.0.0.1 or https; the development worker does nothing.

## Conventions

- Styling: Tailwind utilities in markup; recurring patterns become `@layer components` classes in
  `Styles/app.css` (`.card`, `.btn-primary`, `.input-field`, …). No inline
  `<style>` blocks in components; no component library.
- Theming: two independent choices, both `--th-*` CSS custom properties switched by an attribute on
  `<html>` and mapped to Tailwind tokens via `@theme inline` (`Styles/app.css`, "Colour scheme"):
  - **Mode** (`data-mode`: `dark` / `light`) owns the page: `base`, `surface`, `raised`, `ink*` and
    the chart palette. The header button (`Layout/ModeToggle.razor`) cycles auto → light → dark;
    auto is resolved from `prefers-color-scheme` by the scripts (pre-boot in `index.html`, then
    `fafReplay.applyMode`, which also follows system changes), so CSS only ever sees dark or light.
  - **Faction** (`data-faction`: Cybran default, UEF, Aeon, Seraphim) owns the accents:
    `primary*`, `on-primary`, `accent`, and through them the chrome: buttons, links, active tabs,
    navigation, the logo, focus rings. `edge` (borders), the glow and `nav` (the header background)
    are mixed from `primary` with the mode's neutrals, so they follow both. Every faction has a dark
    and a light step of its accent: a colour readable as text on one page is not on the other.

  **Always use the semantic utilities** (`bg-surface`, `text-ink-muted`, `border-edge`,
  `bg-primary`, …); never hardcode colours in components, or switching breaks. Never give a faction
  its own backgrounds, and never tie a colour to a faction being light or dark; check new UI in
  both modes.
- Menus and other things that open on top of the page are popovers (Popover API: `popover` +
  `popovertarget`, no open/closed state in C#), like the header's menu on narrow screens
  (`.menu-popover`). `js/app.js` closes a popover when a link inside it is taken.
- Page width: header, page and footer share `MainLayout.Container` (`max-w-6xl`). The Build order
  tab compares two players side by side and widens it to a full HD screen (`max-w-[1824px]`).
- Narrow screens (phones, about 390 px wide) are built with Tailwind's breakpoints in CSS, desktop
  unchanged from `sm`/`lg` up. The recurring patterns:
  - **Filter columns** of the list tabs go through `Features/Replay/FilterPanel.razor`: open beside
    the content from `lg`, folded into one summary line above it below that.
  - **Tables:** `.list-table` turns a table into a list below `sm` (the long cell gets
    `.list-table-wide`, the cells `max-sm:p-0`); `.sticky-first-column` keeps the player column of
    a wide table in view while it scrolls sideways. `.tab-strip` is a row of tabs that scrolls.
  - A single-column `grid` needs `grid-cols-1` (`minmax(0, 1fr)`), or its column grows with its
    widest content and the page scrolls sideways.
  - A component renders a different layout per width only when rendering both and hiding one with
    CSS is too heavy: the Build order tab (two maps with hundreds of markers) asks
    `fafReplay.onMediaChange` and shows one player at a time below `xl`.
  - Check a change at 390x844 too (`browser_resize`): no tab may scroll the page sideways
    (`document.documentElement.scrollWidth` equals the viewport width).
- Anything drawn on the map goes through `Features/Replay/MapCanvas.razor`: an SVG in world
  coordinates with the vault preview as backdrop, so replay positions (x, z) are used as-is; its
  `ViewBox` parameter zooms in on a part of the map.
- Map previews come from `Services/MapPreviews.cs`. Generated maps are not in the vault: their
  replay metadata says `"mapname":"None"`, so the folder comes from the scenario's map path, and the
  preview from the map generator's service, asked for every generator version (see the root guide).
  It sends no CORS headers: load it as an image, never with `fetch`. The grid shows until it arrives,
  the question mark when the service cannot render the map.
  The FAF API links no map to a game on a generated map, so a search result has none. Its tile is a
  "Show map" button: `Services/Replays/ReplayMapLookup.cs` downloads the start of the replay (a Range
  request of 64 KB, then 256 KB; `ReplayLoader.TryReadPathToScenario`) and keeps the answer while the
  app runs. Only on a click, never for a whole page. `GameCard` is an `<article>` with a link that
  covers it, since a button inside a link is not allowed; the tile lets clicks through except on its button.
  Replays on disk need none of this: the FAF client writes the real map name in the first line.
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
- Stat icons (Statistics tab: mass, energy, kills, build, redcross): `wwwroot/images/stats/<name>.png`,
  converted at native size (~20 px) from the game's `textures/ui/common/game/unit_view_icons/*.dds` by
  `tools/convert-stat-icons.ps1` (ImageMagick 7; never edit them by hand).
- Unit icons live in `wwwroot/images/units/`, generated from the FAF game repo
  (`textures/ui/common/icons/units/*.dds`) by `tools/convert-unit-icons.ps1` (ImageMagick 7). Never edit
  them by hand; re-run the script. See "Unit icon atlas" below.

## Shareable view state (query parameters)

**The URL is the single source of truth for view state**, so any view of a replay can be shared by
copying the address: `/replays/23225104?tab=chat&players=MarcusM,Printer` opens the Chat tab filtered
to those two players, on any machine.

| Parameter | Meaning | Default when absent |
|---|---|---|
| `tab` | Active replay section: `players`, `stats`, `playthrough`, `buildorder`, `chat`, `events`, `callbacks`, `moderation` (right-aligned, apart from the analysis tabs) | Overview |
| `players` | Comma-separated player names to show; shared by Playthrough, Chat, Events, Callbacks and Moderation | All players |
| `from` / `to` | Game-time window (`12`, `12:30` or `1:02:30`); shared across tabs | See window policy below |
| `types` | Comma-separated input types shown in the Events stream | All types |
| `endpoint` | Selected sim-callback endpoint | The most frequent endpoint |
| `kinds` | Comma-separated entry kinds on the Moderation tab (`Features/Replay/ModerationLog.cs`): `chat`, `selfdestruct`, `giveunits`, `recall`, `pause`, `left`, `focus`, `marker`, `ping`, `drawing`, `server`, `other` | All kinds |
| `pings` | `off` hides pings on the Chat tab (map and feed) | Pings shown |
| `compare` | The two players of the Build order tab, `Left,Right` (unknown names fall back per slot) | The first army vs the first army of another team |
| `view` | Below the maps on the Build order tab: `timings` shows key moments and units ordered per minute, `units` the units (entities) of both players | The order ledger |
| `side` | `right` shows the second player of `compare` on narrow screens, where the Build order tab shows one player at a time; wide screens ignore it | The first player |
| `at` | Playback position of the Playthrough tab (`12`, `12:30` or `1:02:30`); written on pause/seek only, never while playing | `0:00`, paused |

**Window policy** (`TimeWindowFilter.ReadWindow`): one rule on every tab: a window is at most
**four minutes** (`TimeWindowFilter.MaxWindow`), self-correcting with no error states: reversed
bounds swap, a single bound implies the other, To is pulled along when the window is too long.
Events *requires* a window (default `0:00`–`4:00`, kept out of the URL); everywhere else it is
optional (both parameters absent = whole game). Build order ignores the window: it always covers
the first ten minutes. The stepper buttons are always visible and shift
by the window length, shown as their label (−4:00 / +4:00); without an active window the forward
stepper starts one at `0:00`–`4:00` (the back stepper is disabled until there is one).
The **Playthrough tab is exempt** from the window policy: it is a playback view, not a filtered
list: its shareable state is the single `at` instant and it deliberately has no `from`/`to`.
Playback state (playing, speed, current time while playing) lives in component fields; `at` is
written only on pause or seek-while-paused, so playing never floods the URL or re-renders siblings.
Playback pauses when the page is hidden (Page Visibility API, `fafReplay.onPageHidden`), so a
shared `at` is always a moment the user actually saw.
The **Moderation tab is exempt** too: moderators read through the whole game, so it ignores
`from`/`to` (they stay in the URL for the other tabs); its time links open the Events tab on that
player and a 30-second window around the event.
Reuse `Features/Replay/TimeWindowFilter.razor` and `PlayerFilterList.razor` for any new filter panel;
both own their query parameters, and parents re-derive state from the URL in `OnParametersSet`.

**Every component that derives state from the URL must inherit `UrlStateComponent`**
(`Services/UrlStateComponent.cs`, which also provides the protected `Navigation` property; don't
`@inject NavigationManager` on top of it). Gotcha it exists for: a query-only navigation does not
re-render a page whose parameters are unchanged value types (Blazor skips the whole subtree), so a
filter written by one component would never reach its siblings. The base subscribes to
`LocationChanged` and re-runs `OnParametersSet` + render.

The pattern, for any new panel with a selection worth sharing:

- **Read** with `UrlQuery.Get(Navigation, "name")` (`Services/UrlQuery.cs`) inside `OnParametersSet`,
  and derive the full panel state from Model + URL there: every render is then idempotent, and a
  pasted link restores the exact view without extra plumbing.
- **Write** with `Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("name", value), replace: true)`.
  Always `replace: true`: selections must not pollute the browser history.
- **Defaults stay out of the URL**: pass `null` as the value to remove the parameter when the
  selection equals the default (Overview tab, all players, top endpoint). Links stay short and the
  bare URL keeps working.
- **Degrade gracefully**: unknown player names are ignored, an unknown endpoint falls back to the
  default: a stale link to a different replay must never break the page.
- Parameters are independent and may be combined; switching tabs leaves the other parameters alone.

### Guardrails: every view a moderator could point at is a link

Replay links are evidence: moderators send them to (banned) players to show what a decision is
based on, and they end up in moderation records. Treat them as a public contract.

- **Replay pages work signed out.** The replay download is anonymous and all analysis runs in the
  browser, so no tab, panel or parameter may require a login. Data that needs a token (match
  outcome, ratings) is an optional enrichment that is left out silently when signed out, never a
  sign-in prompt in place of the replay.
- **Sort every piece of state into one of two kinds.** Shareable: it changes *what* is shown: tab,
  filters, time window, playback position, and any selection that points at something (a chat
  message, an order, a unit). It goes in the URL. Transient: hover, open menus, scroll position,
  playback while running, animations. It stays in fields. Rule of thumb: if a moderator would say
  "look at *this*", it is shareable.
- **Selection keys come from the replay, not from the page.** Identify an item by data that is the
  same on every machine and in every release: tick + player name (+ ordinal within that tick),
  entity id, blueprint id; never by a list index, render order or a counter assigned while
  loading. A link must select the same item after the list is filtered differently or the app is
  updated.
- **A link restores the view, not just the state:** a selected item from the URL is highlighted
  *and* scrolled into view on load (`scrollToSelected`), on the map as well as in the list.
- **Names and formats are stable.** Never rename or repurpose a parameter or a value; when one has
  to change, keep reading the old form. Players are referenced by name, times in game time
  (`12:30`), tabs by their query value, not by indices that depend on lobby order. The same goes
  for paths: replay pages moved from `/replay/` to `/replays/`, and old links are sent on twice, by
  the server (a permanent redirect in `Program.cs`, for unfurlers and first loads) and by the app
  (`Pages/OldReplayAddress.razor`, for the installed app, whose service worker never asks the server).
- **No hidden inputs.** What is shown may depend only on the replay and the URL. Browser storage
  holds personal preferences only (faction, light/dark mode), never something that filters or selects.
- **The login round trip keeps the full URL.** `AuthService.BeginLoginAsync` stores the address,
  query included, and the callback returns to it. Any new redirect (another provider, an error
  page) must do the same.
- **Check it before merging** a change to a replay tab: set up a view, copy the address, open it in
  a private window (signed out) and confirm the identical view, selection included. Then add the
  parameter to the table above.

## Moderation tab and the AI prompt

The Moderation tab (`Features/Replay/ModerationPanel.razor`) lists everything a moderator reads
through; `Features/Replay/ModerationLog.cs` collects it and builds the AI prompt, so the table and
the prompt always show the same entries.

- **Sources** (`ModerationLog.Collect`): the game's own `ModeratorEvent` log, plus what that log
  leaves out: chat (`GetChatMessages`), pings and markers with their position (`GetPings`; the
  position-less copies in the moderator log are dropped), drawings with the area they cover
  (`GetDrawings`), units given away (`GiveUnitsToPlayer`), recall votes (`SetRecallVote`), pause
  requests and players leaving (`CommandSourceTerminated`). Left out on purpose: the resume every
  client sends at tick 0, chat to `notify` (automatic upgrade notices), and the content of
  `GpgNetSend 'JsonStats'` (kilobytes of end-of-game statistics; reduced to one line, the Callbacks
  tab keeps the full text). Everything else is shown verbatim: it is evidence.
- **Kinds** (`ModerationKind`): the query values of `kinds` are the lowercase enum names, so
  renaming a member breaks links (see "Names and formats are stable"). Every kind, chat included,
  is shown by default; chat, pings, markers and drawings are communication (`IsCommunication`).
- **Player names, not army numbers:** callbacks carry 1-based army numbers (`To`, `From`); entries
  show the player's name, with the number in brackets where it matters.
- **The AI prompt** ("Copy as AI prompt", `ModerationLog.BuildPrompt`) holds the selected players'
  entries of the selected kinds, plus always their communication. It has fixed sections: the
  instructions (summarise per player, cite game times, link every finding, stick to the log, leave
  the decision to the moderator), the game (replay, current view, map with its size), the players
  (army number, team, faction, rating), what each kind means (including the coordinate system),
  how to build links, and the log. The instructions must keep the model from guessing intent or
  deciding on a punishment.
- **The prompt only goes to the clipboard.** The app never sends replay data or chat to an AI
  service itself; the moderator decides where to paste it. The note under the button says the
  prompt contains player names and chat.
- **The prompt's "Links" section is a hand-written copy of the URL contract above** (tabs,
  `players`, `from`/`to`, `at`, `kinds`, the four-minute window, the time format) and is built
  from `Navigation.BaseUri`, so links point to wherever the app runs. When a tab, parameter, kind
  or the window policy changes, update `ModerationLog.AppendLinks` and the kinds list in it in the
  same change, or the AI will produce links that no longer open the right view. Check one generated
  link by opening it.
- New moderation-relevant data goes into `Collect` (with a kind, a label in `Label` and a line in
  the prompt's "What the entries mean"), never only into the panel.

## About pages

`Pages/About.razor` (`/about`) lists explainers for players; each file format gets a card there and
its own page below `about/`: `Pages/AboutReplayFormat.razor` (`/about/replay-format`) and
`Pages/AboutBlueprints.razor` (`/about/blueprints`, told through the Fatboy). The illustrations live in
`Features/About/` (`AnnotatedBytes`, `ByteViewer`, `MessageTape`, `ByteShareBar`, `AboutChapter`,
`AnnotatedBlueprint`). Below them, "Get involved" links to the official FAF places (the client
download, wiki, forums, Discord, YouTube, Twitch, Reddit, contributing, GitHub, donations), the
addresses as linked from faforever.com; take a new or changed one from there too.

- **Audience:** players without technical skills. Explain ideas, not code (no reader classes or
  method names in the text), and build up from what a byte is.
- **Writing style, on request of the owner:** simple English and short sentences. No hyphens,
  underscores or dashes (em or en) in the visible text: write "end of game", not "end-of-game".
  Quoted game files (blueprint and script code) are shown as written. Check the rendered prose,
  e.g. filter the text nodes of `main` outside `.font-mono`, `code` and `pre` for `[-_—–]`.
- **Facts are measured, not estimated:** every number comes from one real example and lives in
  `Features/About/ExampleReplay.cs` (replay 25717491) or `ExampleBlueprint.cs` (release 3839 of the FA
  repository; the Fatboy's blueprint is embedded verbatim as `uel0401_unit.bp`), with how it was
  measured. Re-measure when the example changes.
- `text-base` is a colour here (the `base` token), not a font size: use `text-[16px]`.
- Tailwind only scans `.razor` files, so colour classes picked in code (the tones of
  `AnnotatedBytes.Fill`/`Swatch`) must be literal strings in a `.razor` file. The chart palette is
  available as `viz-1` to `viz-8` (`bg-viz-2`, `border-viz-3/60`, …).

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
  is missing from the index; mods and campaign units often have no icon.
- **Backgrounds:** the game draws a layer background behind each icon, from the blueprint's
  `General.Icon` (`land`, `air`, `sea`, `amph`) plus a state: `_up` (normal), `_over` (hover),
  `_down` (pressed/selected), `UnitIcon`'s `State` parameter. Replays carry no blueprints, so
  `UnitIconAtlas.Layer` derives the layer from the id's third letter (`a` air, `s` sea, else land);
  amphibious units therefore show the land background.
  `cons_bar` is the construction progress overlay, not a layer.
- Icons are transparent PNGs; a few source icons are 32 or 72 px and sit centred (72 px ones scaled
  down) in their 64 px cell. The individual files keep their original size.
- Use the individual files only for one-offs (e.g. a single `<img>` on a detail page).
- After an FA game update, re-run `pwsh tools/convert-unit-icons.ps1 -Source <fa repo>/textures/ui/common/icons/units`
  and commit the result; output is deterministic (PNG timestamps are stripped), so the diff only
  shows real changes. Cell positions can shift when icons are added, so never hardcode coordinates.

## Unit cards

Hovering a unit on the Build order tab (the order ledger, the key moments and the "units ordered"
chips of the Timings view) shows a card with what the unit is: name, faction, tech, where it moves,
roles, cost, build power, health, shield, intel ranges and its weapons. The data is
`wwwroot/data/units/`: an index and one file per distinct set of units, for every release from 3801
(see the [Blueprints guide](../FAForever.FileFormats.Blueprints/AGENTS.md), "Unit data"); regenerate
it, never edit it by hand.

- `Services/Units/UnitDatabase.cs` (scoped, like `UnitIconAtlas`) fetches the index once and each data
  file once, and hands out the data of a game version (`LoadAsync(version)`, resolved through the
  index). `Features/Replay/UnitCardHost.razor` (placed once in `ReplayView`) loads the version the
  replay was played on with the replay page, because reading a file takes about half a second in the
  WebAssembly interpreter, and renders the card.
- A row shows the card with `UnitCardService.Show(blueprintId, x, y)` from `@onmouseenter` (pointer
  position from `MouseEventArgs.ClientX/Y`) and hides it with `Hide()` on `@onmouseleave`; navigation
  hides it too. Hover is transient state: never in the URL. Rows that show a card drop their `title`,
  or the browser's tooltip would cover it.
- **The card must not wait for the tab to render.** Blazor re-renders a component after each of its
  events, and a render of the Build order tab takes 150 to 250 ms in a debug build (two maps with
  hundreds of markers, the ledger). `BuildOrderPanel` and `BuildOrderTimings` therefore implement
  `IHandleEvent` and skip that render for hover events, so the card host renders alone (a few ms).
  The maps highlight the hovered order once the pointer rests 60 ms (`HighlightWhenResting`), so
  moving across the ledger renders the tab once where it stops, not twice per row.
- `fafReplay.placeNear` puts the card beside the pointer and keeps it inside the viewport; the card
  ignores the pointer (`pointer-events-none`), so it never takes the hover away from the row.
- `Features/Replay/UnitCard.razor` shows only real weapons (not `Death`, `Teleport` or weapons without
  damage) and merges identical ones ("2 × Electron Autocannon"). Roles and the motion type label come
  from `Services/Units/UnitRoles.cs`; gunships have no category of their own (`AIR` + `GROUNDATTACK`).
- **The data is of the FAF branch without mods.** The card uses the replay's own game version (from
  the header, `UnitDataNotes.GameVersion`). `Services/Units/UnitDataNotes.cs` adds a note when a
  replay ran on another featured mod (`Metadata.FeaturedMod` is not `faf`), outside FAF (header version
  not `v1.50.x`, e.g. Steam), on a version without data of its own (before 3801, or newer than the
  data), or with mods (`Header.Mods`). Units missing from the data (mods, campaign) get a short card
  saying so.
- The published service worker caches `data/units/index.json` with the app shell, and each data file
  the first time the app reads it (not all of them on install); an app update starts a new cache.

## Replays tab

Built like the Maps and Units tabs: `Pages/Replays.razor` (`/replays`) is an entry page with a card
per way to a replay (same writing style as the About pages), each page links back ("← Replays").
Paths are in `Features/Search/ReplayLinks.cs`.

| Page | Path | What |
|---|---|---|
| `Pages/Search.razor` | `replays/search` | The vault search (needs a login). Rated games only by default (`rated=false` to include the rest: 4.2 of 19.2 million games are rated, and the filter makes every search faster). |
| `Pages/ReplayFolder.razor` | `replays/folder` | The replays in a folder on this computer. **Installed app only**, on request of the owner, to encourage installing (`fafReplay.isInstalled`, display mode standalone); in a browser tab the card and the page explain how to install. |
| `Pages/LocalReplayPage.razor` | `replays/local` | One replay file: the replay when one is in memory, otherwise a file picker. |

The replay folder:

- **A folder input, not the File System Access API.** `showDirectoryPicker` refuses the FAF client's
  folder, `C:\ProgramData\FAForever\replays` ("it contains system files", checked 2026-10-06); an
  `<input type="file" webkitdirectory>` reads it (641 files). The price: no lasting access, so the
  folder is picked again after a reload, and the browser asks whether to "upload" the files (nothing
  is uploaded; the page says so). The folder is hidden: the page tells to paste the path into the
  dialog's address bar.
- **Only the first line of each file is read** (js/app.js `watchReplayFolder`): a .fafreplay starts
  with its JSON metadata (title, map folder, teams, start and end, featured mod, `uid`). 641 files
  take about a second. `Services/Replays/LocalReplayFolder.cs` parses it and keeps the list while
  the app runs, so a replay and back keeps the folder; the files stay in JS (`folderFiles`) until one
  is opened in full. The FAF client names its files `{game id}-{player}.fafreplay`, the id for files
  without a metadata line (.scfareplay).
- **Nothing of it is in the URL**: the files only exist on this computer. What can be shared is the
  vault page of a game: every card links it (`replays/{uid}`), and a local .fafreplay with a `uid`
  shows "vault #id" and a Share button for its vault page, with the current tab and filters
  (`ReplayView.VaultId`, `ShareButton.Path`). Offline games have no `uid` and no link.

## Maps tab

The Maps tab is built like the Units tab: `Pages/Maps.razor` (`/maps`) is an entry page with a card
per page below it (same writing style as the About pages, and it draws no data, so it works signed
out), and each page links back ("← Maps"). Paths are in `Features/Maps/MapLinks.cs`.

| Page | Path | What |
|---|---|---|
| `Pages/FeaturedMaps.razor` | `maps/featured` | The featured maps, chosen by the FAF team (`recommended` in the API, 33 visible), one page, most played first. The app calls them featured everywhere, never recommended. |
| `Pages/LadderMaps.razor` | `maps/ladder` | The matchmaker's current pools: a tab per queue (`queue`, the technical name, default the first: 1v1), a chip per rating bracket (`bracket`, default all). |
| `Pages/MapSearch.razor` | `maps/search` | Every visible map, with filters (parameters below). |

Maps are cards (`Features/Maps/MapCard.razor`): the preview with size and players, name, version,
author, games played, rating and the start of the description, and an optional slot at the bottom
(the ladder page puts the brackets there). The data is `/data/map` with a version, author and review
summary (`FafApiClient.SearchMapsAsync`, models in `Services/Api/MapModels.cs`), so the three pages
need a login like the Replays tab. Cards do not link anywhere yet: a map page waits for the map parser.
The one way out is "Replays on this map" (`MapLinks.Replays`): the replay search on the map's name in
a month around today (`around` + `within=month`). Keep the window: without it a popular map takes
14 s (FAForever/faf-java-api#1182), with it 0.3 s; the search page can widen it.

The ladder pools (`FafApiClient.GetLadderPoolsAsync`) are one request:
`matchmakerQueueMapPool` with its queue, pool, assignments, versions and maps (0.3 s, 23 pools in 5
queues). A pool holds a **version** of a map, which may be hidden in the vault (two were, on
2026-10-06); the ladder page shows them anyway, as the matchmaker plays them. A bracket's label is the
last word of the pool's name ("TMM 2v2 300-800" gives `300-800`): the pool's own `minRating` and
`maxRating` are on another scale (that pool has 500 to 1000) and are not shown. Generated maps have
no map, only `mapParams` (`neroxis`, size in game units, spawns, generator version), shown by
`Features/Maps/GeneratedMapCard.razor`; the 3v3 queue has only those, in sizes between the steps
(576 units is 11.25 km). Queue names come from `LadderQueue.Name` (a new queue shows its technical name).

The search page's parameters:

| Parameter | Meaning | Default when absent |
|---|---|---|
| `q` | Part of the map name | No name filter |
| `author` | The author's exact login, `*` as wildcard | Any author |
| `size` | Sizes in kilometres, comma separated: `1.25`, `2.5`, `5`, `10`, `20`, `40`, `81` | All sizes |
| `players` | Player counts, comma separated: `2`, `4`, `6`, `8`, `10`, `12`, `16` | Any |
| `ranked` | `1`: only maps whose latest version counts for rating | All |
| `featured` | `1`: only the featured maps | All |
| `sort` | `rating` (lower bound of the review score), `newest` (upload of the latest version), `name` | Most played |
| `page` | Page of 24 maps | 1 |

What the data is like (measured 2026-10-06, with a login):

- **Hidden maps are never shown**, on request of the owner: every query filters on
  `latestVersion.hidden==false`. That also leaves out the original game maps (Seton's Clutch is
  `scmp_009`, hidden because it comes with the game, and the most played map of all): players do
  not expect them here. `latestVersion` is the highest version number, hidden or not, so a map
  whose newest upload is hidden is gone from the list. 14,803 maps, 9,277 visible.
- **Sizes are game units, shown in kilometres** as the lobby does: 64 = 1.25 km, 128 = 2.5, 256 = 5,
  512 = 10, 1024 = 20, 2048 = 40, 4096 = 81 km (`MapSizes`). Every visible map has one of these
  widths. Filter like the FAF client: `latestVersion.width=in=("256")`.
- **A map's `updateTime` changes with every game played on it**, so "newest" sorts on
  `latestVersion.createTime`. `downloads` and `draws` are always 0 (a view since FAForever/db V116);
  only `gamesPlayed` is real.
- **Descriptions** may start with a `<LOC key>` (a translation key of the game) and run to about a
  thousand characters with line breaks: `MapSummary.ReadableDescription` drops the key, the card
  clamps it to three lines.
- **Speed:** every filter and sort here answered in 0.1 to 0.2 s uncached; the slow map search of
  the Replays tab (FAForever/faf-java-api#1182) is about games, not maps.
- **Previews:** the vault's large preview (512 px; the small one is 128 px, too blurry for a card),
  loaded lazily. The content server serves previews and map zips anonymously with CORS `*` and
  Range requests, which the map parser can use to read single files from a zip.

## 404 page

`App.razor`'s `NotFound` renders `Features/NotFound/NotFoundView.razor`: a random message of the
chosen faction (`NotFoundMessages.cs`, four or five per faction), with the icon of the unit it is about
(linking to the unit database) and sometimes a "Data-Link" tip, the boxed tips of the manual.

- The jokes come from the game manuals: the
  [Supreme Commander manual](https://manuals.thqnordic.com/SupremeCommander/SupremeCommander_PC_Manual_EN.pdf)
  for UEF, Cybran and Aeon (faction histories, characters, unit descriptions) and the
  [Forged Alliance manual](https://manuals.thqnordic.com/SupremeCommander/SupremeCommanderFA_OnlineManual_DE.pdf)
  (German), the Forged Alliance campaign (the commanders' lines and taunts in the FA repository's
  `loc/US/strings_db.lua` and `lua/ui/game/taunt.lua`; untranslated Seraphim lines read "[Language Not Recognized]") and the Seraphim
  language that their unit names are made of
  ([word list](https://supcom.fandom.com/wiki/Seraphim_Language): `aez` command, `esel` radar,
  `hyal` Mass, `atoh` extractor, ...). Keep new ones to what these sources say, so players recognise them.
  The manuals predate the release and FAF's balance changes, so check a claim about a unit
  against its blueprint in the FA repository (the manual's Mantis "repairs itself"; the game's
  only repairs others).
- Switching faction on the page picks a message of the new faction; "Another message" picks a
  different one. The choice is not in the URL: the page has no state worth sharing.
- The unit ids must be in the icon atlas (a missing one shows the placeholder icon).

## Units landing page

`Pages/Units.razor` (`/units`, the Units tab in the header) is an entry page like About: a card for
the unit database and one for the unit history, in the same writing style as the About pages. Both
pages link back to it ("← Units") and to each other (History in the details panel and the
comparison, "Show in the database" on a history card). It keeps the reading width; the pages below
`units/` widen to full HD. Below the cards, "Did you know?" (`Features/Units/UnitFactStrip.razor`)
shows facts counted from the data (`Services/Units/UnitFacts.cs`: most changed unit, latest
changes, units untouched the longest, longest wait, releases without unit changes) and a card that
leads to the blueprints explainer, as a row of cards that scrolls sideways (`fafReplay.scrollStrip`). The facts from the index show at once; those
about units wait for the latest data file (names, buildable only) behind a placeholder. A fact links
to what it is about: the history of its units, or the database of a version with `changed=1`.
The facts leave out releases 3761 and 3762 (`UnitFacts.IsLeftOut`: one took categories off most
units, the next put them back); the heading says "most releases".
`/units` used to be the database: a link to it with a query
(`/units?unit=uel0201`) is sent on to `units/database` with that query, so old links keep working.

## Unit database

`Pages/UnitDatabasePage.razor` (`units/database`, `UnitLinks.Database`) lists the units of one game version, the
latest unless `version` says otherwise (a picker in the heading, with the release date and how many
units changed since the previous version, from the index entry): a filter column, a sortable table, a
comparison and a details panel. The pieces live in `Features/Units/`.
The page widens to full HD like the Build order tab (`MainLayout.Container`).

- **The URL holds every choice**, like the replay pages (same guardrails: replace, defaults stay out,
  unknown values are ignored, names never change). Filter groups and their option slugs are defined
  once in `UnitFilters` (roles come from `UnitRoles.All`); within a group a unit needs one of the
  chosen options, across groups all of them.

  | Parameter | Meaning | Default when absent |
  |---|---|---|
  | `q` | Text in the name, description or id (written after a 250 ms pause in typing) | No text filter |
  | `faction` | `uef`, `aeon`, `cybran`, `seraphim` | All factions |
  | `tech` | `1`, `2`, `3`, `4` (experimental); commanders have no tech level | All |
  | `move` | `land`, `amphibious`, `hover`, `air`, `naval`, `sub`, `structure` (from `MotionType`) | All |
  | `role` | The slugs of `UnitRoles.All`: `engineer`, `bomber`, `gunship`, `transport`, `shield`, ... | All |
  | `weapon` | `direct`, `indirect`, `antiair`, `antinavy`, `defense` (the weapon's `RangeCategory`), `none` | All |
  | `intel` | `radar`, `sonar`, `omni` | All |
  | `all` | `1` also lists units no player can build (campaign, civilian, helpers) | Buildable units only |
  | `changed` | `1` lists only the units changed or added in this version (the index entry's `changes`) | All units |
  | `sort` | `name`, `mass`, `energy`, `time`, `health`, `speed`, `range`, `vision`; a leading `-` sorts high to low | Faction, tech, name |
  | `unit` | The unit in the details panel (lower case blueprint id); its row scrolls into view | None |
  | `compare` | Up to six unit ids side by side, in order | No comparison |
  | `version` | The game version, e.g. `3830`; one without data of its own shows the nearest (`UnitDataIndex.Resolve`) | The latest |

  The history page (`units/history`, below) has one parameter: `units`, the unit ids in order.

- A replay's overview links its game version here (`OverviewPanel`: the version number is the
  link, with `version=`), for FAF versions from `UnitDatabase.FirstVersion` on; older ones would show another
  version's units, so they get no link.
- The details panel (`UnitDetail`) shows the unit card and the build tree around the unit: upgrades
  from and to, built by, builds. Every unit there is a link (`unit=`), so the tree can be walked; for a
  buildable unit, builders that no player can build are left out.
- The comparison (`UnitComparison`) marks the best value of a row (lowest cost, highest anything else)
  only when at least two units have different values. "Range" is the longest range of the unit's real
  weapons (`UnitRoles.IsRealWeapon`, the same rule as the card).
- Reading a data file takes about half a second and rendering all ~400 rows a few hundred ms in a
  debug build; a filter change re-renders only the rows that pass. `UrlStateComponent` re-runs only
  the synchronous `OnParametersSet`, so the page derives its view there and starts loading another
  version from there when `version` changes.

## Unit history

`Pages/UnitHistory.razor` (`units/history?units=uel0201,url0107`) shows units across every game version,
a card each (`Features/Units/UnitHistoryCard.razor`), in the order of `units` (at most six, like the
comparison; unknown ids get a short card; each card's × takes its unit out). The details panel's
"History" opens it for that unit, the comparison's "History" for the compared ones
(`UnitLinks.History`), and a search box next to the heading (`Features/Units/UnitSearchBox.razor`)
adds a unit: an ARIA combobox that suggests units of the latest version on id, name and description
as you type (id prefix first, buildable units before others, at most 8), picked with the arrow keys
and Enter or the mouse (on `mousedown`, which comes before the input's blur). A card has a column per stretch of versions in which the unit stayed the same,
a row per value (`Services/Units/UnitStats.cs`: the same labels for every version, one line per weapon
value), changed values marked against the column before (▲/▼ for numbers), and a Categories row with
what was added or removed. Column headers link to that version in the unit database.

- `UnitDatabase.LoadHistoryAsync` finds the stretches from the index alone (a version starts a new
  one where its `changes` lists the unit as changed or added, ends one where removed) and reads only
  those versions, and of each file only the unit's line (`UnitData.ReadUnit`), so a unit that changed
  ten times costs ten small reads, not ten files of 600 units.
- A column that looks the same as the one before means a change in a value the table does not show;
  `UnitSummary` evens out rewrites without a change in meaning (see the Blueprints guide), so this
  should be rare.

## Browser automation (Playwright MCP)

`.mcp.json` (repo root) registers the official Playwright MCP server (`npx -y @playwright/mcp@latest`), so agents
can drive the Viewer in a real browser: navigate, snapshot the accessibility tree, click, read console
output and inspect network traffic.

- **Start the app first**: the MCP server only drives a browser, it does not host anything. Run the
  `server` task (or `dotnet watch --project src/FAForever.Vault.Server`) and navigate to
  `http://127.0.0.1:5080`; the fixed port matters for OAuth (see the root guide).
- Project-scoped MCP servers need a one-off approval per machine. Accept the prompt on the next
  Claude Code start, or run `claude mcp list` to check the connection.
- Screenshots, traces and downloads land in `.playwright-mcp/` at the repo root (gitignored). The viewport defaults to
  1440x900; resize to 390x844 to check narrow screens (see Conventions).
- `.claude/settings.json` pre-approves the navigation, inspection and interaction tools.
  `browser_evaluate`, `browser_run_code_unsafe` and `browser_file_upload` deliberately still prompt.
- The browser profile is temporary: an FAF login does **not** survive a browser restart. For a
  persistent session add `--user-data-dir=.playwright-mcp/profile` to the args; that stores real
  session cookies on disk, so keep the directory gitignored.
- This is an inspection tool, not a test suite. There is no Playwright test project; automated
  coverage lives in the MSTest projects under `tests/`.
