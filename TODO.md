# TODO: items that need the project owner

- [ ] **Request a dedicated OAuth client from the FAForever team.** We currently reuse the official
  desktop client's public client id (`2e8808cf-5889-469b-b2c3-01f0cc58c4af`), which only allows
  loopback redirect URIs without a path (hence the fixed `http://127.0.0.1:5080` dev origin). A proper
  client needs: public client, PKCE S256, redirect URIs for localhost + the production origin, scopes
  `openid offline public_profile`, and ideally `allowed_cors_origins` so the token proxy can be dropped.
- [x] ~~Host https://vault.jipwijnia.nl~~: live since 2026-10-03, with automated deploys from
  `deploy/production`; the server is configured in
  [Garanas/jipwijnia-vps](https://github.com/Garanas/jipwijnia-vps). Signing in (and so Search)
  still needs the dedicated client above, registered with redirect URIs
  `https://vault.jipwijnia.nl/` and `http://127.0.0.1`; the server proxies the token exchange, so
  no CORS change at FAF is needed. `wwwroot/appsettings.Production.json` already holds the
  production redirect URI.
- [ ] **Retire the old GitHub Pages site** (https://garanas.github.io/scfa-cs-replay/, the old
  MudBlazor viewer served from `live/gh-pages`): unpublish Pages, or replace it with a redirect to
  the vault, and delete the branches `live/gh-pages` and `deploy/gh-pages`.
- [x] ~~Verify the login flow against Hydra with a real FAF account~~: verified 2026-10-03: sign-in
  via the loopback redirect works, `/me` resolves the player name, and `/search` returns live results.
- [x] ~~Verify the FAF API attribute/filter names with live calls~~: verified 2026-10-03 with a real
  session: `playerStats.player.login` filtering, `page[totals]`, `name`/`startTime`/`endTime`,
  `mapVersion.map.displayName`, `thumbnailUrlSmall`, `featuredMod`, `faction`/`team`/
  `beforeMean`/`beforeDeviation` all behave as modelled. Bonus for the future result feature:
  `gamePlayerStats` carries `result` ("VICTORY"/"DEFEAT"), `score` and `afterMean`/`afterDeviation`.
- [ ] **Show the match outcome on vault replays** (now unblocked): fetch `/data/game/{id}` with
  `playerStats.player` when signed in and merge into the replay page: winner badges in Players,
  rating changes (`beforeMean/afterMean`), validity. The replay file itself never knows who won.
- [x] ~~Map blueprint ids to unit names/icons~~: icons via the sprite atlases (`UnitIcon`), names
  via `src/FAForever.FileFormats.Blueprints/UnitNames.g.cs`, generated from the game repo's blueprint Description
  fields by `tools/generate-unit-names.ps1`.
- [ ] **Put the remaining selections in the URL** (see the guardrails in AGENTS.md, "Shareable view
  state"): the selected chat message or drawing (`ChatPanel.selected`), the selected order
  (`BuildOrderPanel.selected`) and the selected unit on the Units view (`BuildOrderPanel.selectedEntity`)
  live in fields, so a shared link loses them. The entity id is already replay data; the chat and
  order keys are list indices assigned while loading, so those need a key from replay data first
  (tick + player + ordinal).
- [x] ~~Review the desync flag~~: fixed 2026-10-03: the checksum comparison never advanced past
  tick 0 and compared the first checksum with 0, so every replay read as desynced. Every client
  records a checksum of the same tick every 50 ticks; `InSync` is now false only when they differ
  (`SCFADesyncTest` covers a forged mismatch). All test assets are in sync.
- [x] ~~`LuaDataLoader` boolean parsing~~: confirmed inverted and **fixed** (`!= 0`): with the old
  read, rated human players carried `Human=false` and civilian armies `Human=true` in every test
  replay. Note: faf-java-commons `LoadUtils.parseLua` has the same inversion (`== 0`), worth
  reporting upstream.
- [ ] **Make a render of the Build order tab cheaper.** Every event on the tab re-renders all of
  it: both maps with hundreds of markers and the ledger, 150 to 250 ms in a debug build (measured
  2026-10-04 on replay 25717491). Hovering an order no longer waits for it (`IHandleEvent` in
  `BuildOrderPanel`, see the Viewer guide, "Unit cards"), but hovering an entity on the Units view
  and selecting an order still do. Idea: move the map markers and ledger rows into child components
  that only re-render when their own highlight or selection changes, and measure again in a
  release build.
- [ ] **Turn on automatic unit data updates.** `.github/workflows/update-unit-data.yml` regenerates
  `wwwroot/data/units.json` for a new FA release and opens a pull request. It needs: (1) Settings →
  Actions → General → "Allow GitHub Actions to create and approve pull requests"; (2) for an update
  right after a release instead of the daily check, a workflow in
  [FAForever/fa](https://github.com/FAForever/fa) that sends a `repository_dispatch` (`fa-release`,
  the tag as `client_payload.version`) with a token that may do so here; the snippet is at the top
  of the workflow file. That needs the FAF team's agreement. The workflow has not run on GitHub yet:
  trigger it by hand once (Actions → Update unit data → Run workflow) to check it.
