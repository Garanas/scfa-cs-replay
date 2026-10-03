# TODO — items that need the project owner

- [ ] **Request a dedicated OAuth client from the FAForever team.** We currently reuse the official
  desktop client's public client id (`2e8808cf-5889-469b-b2c3-01f0cc58c4af`), which only allows
  loopback redirect URIs without a path (hence the fixed `http://127.0.0.1:5080` dev origin). A proper
  client needs: public client, PKCE S256, redirect URIs for localhost + the production origin, scopes
  `openid offline public_profile`, and ideally `allowed_cors_origins` so the token proxy can be dropped.
- [ ] **Enable sign-in on GitHub Pages.** The viewer is deployed as a static site to
  https://garanas.github.io/scfa-cs-replay/ (`.github/workflows/pages.yml`); replays from the vault
  and local files work, signing in (and so Search) does not: there is no token proxy there and the
  redirect URI is pinned to `http://127.0.0.1:5080`. Needs the dedicated client above (with this
  origin as redirect URI and CORS origin) and a production `appsettings` that calls the token
  endpoint directly.
- [ ] **Remove the old Pages branches** `live/gh-pages` and `deploy/gh-pages` once the Actions
  deployment runs, and `.github/pages/service-worker.js` some months later (it retires the old
  deployment's service worker in returning browsers).
- [x] ~~Verify the login flow against Hydra with a real FAF account~~ — verified 2026-10-03: sign-in
  via the loopback redirect works, `/me` resolves the player name, and `/search` returns live results.
- [x] ~~Verify the FAF API attribute/filter names with live calls~~ — verified 2026-10-03 with a real
  session: `playerStats.player.login` filtering, `page[totals]`, `name`/`startTime`/`endTime`,
  `mapVersion.map.displayName`, `thumbnailUrlSmall`, `featuredMod`, `faction`/`team`/
  `beforeMean`/`beforeDeviation` all behave as modelled. Bonus for the future result feature:
  `gamePlayerStats` carries `result` ("VICTORY"/"DEFEAT"), `score` and `afterMean`/`afterDeviation`.
- [ ] **Show the match outcome on vault replays** (now unblocked): fetch `/data/game/{id}` with
  `playerStats.player` when signed in and merge into the replay page — winner badges in Players,
  rating changes (`beforeMean/afterMean`), validity. The replay file itself never knows who won.
- [x] ~~Map blueprint ids to unit names/icons~~ — icons via the sprite atlases (`UnitIcon`), names
  via `FAForever.Replay/UnitNames.g.cs`, generated from the game repo's blueprint Description
  fields by `tools/generate-unit-names.ps1`.
- [ ] **Review the desync flag.** Both test replays shown in the browser (vault #22338092 and a
  one-player .scfareplay) render a "desync" badge, which is suspicious for single-player games —
  `ReplayBody.InSync` may be a false positive.
- [x] ~~`LuaDataLoader` boolean parsing~~ — confirmed inverted and **fixed** (`!= 0`): with the old
  read, rated human players carried `Human=false` and civilian armies `Human=true` in every test
  replay. Note: faf-java-commons `LoadUtils.parseLua` has the same inversion (`== 0`) — worth
  reporting upstream.
