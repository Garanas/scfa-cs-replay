# TODO — items that need the project owner

- [ ] **Request a dedicated OAuth client from the FAForever team.** We currently reuse the official
  desktop client's public client id (`2e8808cf-5889-469b-b2c3-01f0cc58c4af`), which only allows
  loopback redirect URIs without a path (hence the fixed `http://127.0.0.1:5080` dev origin). A proper
  client needs: public client, PKCE S256, redirect URIs for localhost + the production origin, scopes
  `openid offline public_profile`, and ideally `allowed_cors_origins` so the token proxy can be dropped.
- [ ] **Verify the login flow against Hydra with a real FAF account** (needs a human with credentials):
  home → Sign in → FAF login page → redirected back → profile visible → `/search` works.
- [ ] **Verify the FAF API attribute/filter names with live calls** (needs a signed-in session):
  `FafApiClient` follows the faf-java-api models (`name`, `startTime`, `endTime`,
  `playerStats.player.login`, `mapVersion.map.displayName`, `featuredMod.technicalName`,
  `beforeMean`/`beforeDeviation`, `faction`, `team`, `result`, `thumbnailUrlSmall`), but reads
  require OAuth so they could not be verified while building. Search for `TODO(api-attributes)`.
- [ ] **Map blueprint ids to unit names/icons** in the build-order table (static lookup, e.g.
  generated from the FAF unit database). Search for `TODO(unit-names)`.
- [ ] **Review the desync flag.** Both test replays shown in the browser (vault #22338092 and a
  one-player .scfareplay) render a "desync" badge, which is suspicious for single-player games —
  `ReplayBody.InSync` may be a false positive.
- [x] ~~`LuaDataLoader` boolean parsing~~ — confirmed inverted and **fixed** (`!= 0`): with the old
  read, rated human players carried `Human=false` and civilian armies `Human=true` in every test
  replay. Note: faf-java-commons `LoadUtils.parseLua` has the same inversion (`== 0`) — worth
  reporting upstream.
