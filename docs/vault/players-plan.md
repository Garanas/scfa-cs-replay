# Players: plan

A Players section for the Vault viewer: player profiles (ratings, rating history, matches, army
statistics, achievements), leaderboard rankings, league standings and a page that compares two
players. Every number comes from the FAForever API (`https://api.faforever.com/data/{entity}`,
JSON:API with RSQL filters, Bearer token), which the app already calls for replays and maps.

The screen designs: the Design canvas "Player Analytics Screens"
(https://claude.ai/artifact/H5GbDUPKLo9zHhE1EWgH8m); a copy of its artboards as plain HTML lives in
`D:\wt\ref\players-design\` (one `*.dc.html` per screen; the layout, labels and chart choices are the
spec, the inline styles are not: the app uses Tailwind and its semantic tokens).

## Pages and addresses

Links are base-relative (`href="players/123"`). Paths live in `Features/Players/PlayerLinks.cs`.

| Page | Path | Query parameters (defaults stay out of the URL) |
|---|---|---|
| Players landing | `players` | `q` (search text) |
| Profile, Overview | `players/{id}` | `board` (leaderboard technical name of the chart; default: the board with most games) |
| Profile, Rating history | `players/{id}?tab=rating` | `board`, `period` (`1w`, `1m`, `3m`, `1y` default, `all`), `page` |
| Profile, Matches | `players/{id}?tab=matches` | `period`, `mode` (`ladder_1v1`, `tmm_2v2`, `tmm_4v4_full_share`, `custom`, `all` default), `faction`, `result` (`win`, `loss`), `map`, `with` (player name), `sort`, `page` |
| Profile, Army and factions | `players/{id}?tab=army` | `show` (achievements: `unlocked`, `progress`, `locked`) |
| Rankings | `players/rankings` | `board` (default `ladder_1v1`), `settled` (`0` shows σ above 100 too), `active` (`30d`, `90d` default, `1y`, `all`), `page`, `q` |
| Leagues | `players/leagues` | `league` (default the ladder 1v1 league), `season` (id), `subdivision` (id, the standings shown) |
| Compare | `players/compare` | `a`, `b` (player ids), `board`, `period` |

A profile is addressed by the numeric player id: names change, ids do not, and the API resolves a
name to an id in one call (`/data/player?filter=login=="Name"`). The landing page's search looks at
current names and former names (`nameRecord`).

Every page with view state inherits `UrlStateComponent`, reads with `UrlQuery.Get`, writes with
`GetUriWithQueryParameters` + `NavigateTo(replace: true)`, and gets a `ShareButton`. The URL table
in `src/FAForever.Vault.Viewer/AGENTS.md` ("Players tab") lists every parameter.

All Players pages need a signed-in user (the API refuses anonymous calls). Signed out they show the
login gate card, like the vault search; nothing else is hidden behind a login.

## Data

Rating shown anywhere is μ − 3σ, through `ReplayPlayerOptions.DisplayRating(mean, deviation)`
(FileFormats.Replay), rounded. Ticks are 10 per second (`replayTicks` / 10 = seconds).

| Need | Request |
|---|---|
| Profile | `GET /data/player/{id}?include=names,clanMembership.clan,currentAvatar.avatar` (the avatar through its assignment; unverified) |
| Find a player | `GET /data/player?filter=login=="*{q}*"&include=clanMembership.clan&page[size]=10` and `GET /data/nameRecord?filter=name=="*{q}*"&include=player,player.clanMembership.clan&page[size]=10`; by exact name: `filter=login=="{name}"&page[size]=1` |
| Leaderboards | `GET /data/leaderboard` (cached for the session) |
| Ratings of a player | `GET /data/leaderboardRating?filter=player.id=={id}&include=leaderboard` |
| Rating history | `GET /data/leaderboardRatingJournal?filter=gamePlayerStats.player.id=={id};leaderboard.technicalName=="{board}";gamePlayerStats.scoreTime=ge={since}&include=gamePlayerStats,gamePlayerStats.game,gamePlayerStats.game.mapVersion.map,gamePlayerStats.game.playerStats,gamePlayerStats.game.playerStats.player&sort=-gamePlayerStats.scoreTime&page[size]=1000` (loop over pages) |
| Matches | `GET /data/gamePlayerStats?filter=player.id=={id};scoreTime=ge={since}&include=game,game.mapVersion.map,game.featuredMod,game.playerStats.player,ratingChanges.leaderboard&sort=-scoreTime&page[size]=25&page[number]={n}&page[totals]`; the mode filter goes through `ratingChanges.leaderboard.technicalName=="{board}"`; custom games are the `global` board's (`PlayerRequests.BoardOfMode`; verify live); faction as `faction=={1..4}`, result as `result==VICTORY`, with-player as `game.playerStats.player.login=="{name}"`; the chart lists use the same filter with `page[size]=1000` and without `game.playerStats` |
| Lifetime counters | `GET /data/playerEvent?filter=player.id=={id}&include=event&page[size]=100` (paged until a short page) |
| Achievements | `GET /data/achievement?sort=order&page[size]=100` (cached) and `GET /data/playerAchievement?filter=player.id=={id}&include=achievement&page[size]=100` |
| Rankings | `GET /data/leaderboardRating?filter=leaderboard.technicalName=="{board}";totalGames=gt=0[;deviation=lt=100][;updateTime=ge={since}]&include=player,player.clanMembership.clan&sort=-rating&page[size]=50&page[number]={n}&page[totals]` |
| Rank of a player | the same filter plus `;rating=gt={rating}` with `page[size]=1&page[totals]`: rank = totalRecords + 1 |
| Rating distribution | one count request per 100-point bin (`rating=ge=X;rating=lt=Y`, `page[size]=1&page[totals]`), 24 bins in parallel, cached per board for the session |
| Leagues and seasons | `GET /data/league?filter=enabled==true`, `GET /data/leagueSeason?include=league,leaderboard&sort=-startDate` (cached) |
| Division layout | `GET /data/leagueSeasonDivisionSubdivision?filter=leagueSeasonDivision.leagueSeason.id=={season}&include=leagueSeasonDivision&sort=leagueSeasonDivision.divisionIndex,subdivisionIndex` |
| League entry of a player | `GET /data/leagueSeasonScore?filter=loginId=={id};leagueSeason.id=={season}&include=leagueSeason,leagueSeasonDivisionSubdivision.leagueSeasonDivision` (all active seasons: `leagueSeason.endDate=ge={now}` instead of the season id) |
| Players per subdivision | one count request per subdivision (`leagueSeason.id=={season};leagueSeasonDivisionSubdivision.id=={sub}`, `page[size]=1&page[totals]`) |
| Standings of a subdivision | `GET /data/leagueSeasonScore?filter=leagueSeason.id=={season};leagueSeasonDivisionSubdivision.id=={sub}&sort=-score,-gameCount&page[size]=50&page[totals]`; player names come from `GET /data/player?filter=id=in=(…)` (the score has only `loginId`) |
| League journey | `GET /data/leagueScoreJournal?filter=loginId=={id};leagueSeason.id=={season}&include=leagueSeasonDivisionSubdivisionBefore.leagueSeasonDivision,leagueSeasonDivisionSubdivisionAfter.leagueSeasonDivision&sort=gameId` |
| Head to head | `GET /data/game?filter=playerStats.player.id=={a};playerStats.player.id=={b};startTime=ge={since}&include=playerStats.player,mapVersion.map&sort=-startTime` (verify that Elide treats the two predicates as two different rows; if not, intersect both players' match lists in the browser) |

Entity attributes, from the API's source (`faf-java-api`, `data/domain` and `league/domain`), all
camelCase; relationships in brackets:

- `player`: `login`, `userAgent`, `createTime`, `updateTime` [`names` → `nameRecord`: `name`, `changeTime`; `clanMembership` → `clan`: `name`, `tag`, `description`, `tagColor`, `websiteUrl`, `createTime`, [`leader`, `founder`, `memberships`]; `avatarAssignments` → `avatar`: `url`, `tooltip`; `currentAvatar`]. `email`, `recentIpAddress`, `lastLogin` are readable only by the player or a moderator: never rely on them.
- `leaderboard`: `technicalName`, `nameKey`, `descriptionKey`. Display names are ours (`Leaderboards.DisplayName`): `global` "Global", `ladder_1v1` "Ladder 1v1", `tmm_2v2` "2v2", `tmm_4v4_full_share` "4v4 full share", `tmm_4v4_share_until_death` "4v4 share until death", anything else from the technical name (`tmm_3v3` → "3v3").
- `leaderboardRating`: `mean`, `deviation`, `rating`, `totalGames`, `wonGames`, `createTime`, `updateTime` [`player`, `leaderboard`].
- `leaderboardRatingJournal`: `meanBefore`, `deviationBefore`, `meanAfter`, `deviationAfter`, `createTime` [`leaderboard`, `gamePlayerStats`].
- `gamePlayerStats`: `ai`, `faction` (`UEF`, `AEON`, `CYBRAN`, `SERAPHIM`, others possible), `color`, `team`, `startSpot`, `beforeMean`, `beforeDeviation`, `afterMean`, `afterDeviation`, `score`, `scoreTime`, `result` (`VICTORY`, `DEFEAT`, `DRAW`, `MUTUAL_DRAW`, `UNKNOWN`, `CONFLICTING`; the Viewer already maps these in `GameModels.cs`) [`player`, `game`, `ratingChanges` → journal].
- `game`: `name`, `startTime`, `endTime`, `replayTicks`, `victoryCondition`, `validity`, `replayUrl`, `replayAvailable` [`host`, `featuredMod`, `mapVersion` → `map`, `playerStats`].
- `playerEvent`: `currentCount` [`player`, `event`: `name`, `imageUrl`, `type`]. The event ids are fixed GUIDs (`PlayerEvents` constants): custom games played `cfa449a6-655b-48d5-9a27-6044804fe35c`, ladder games played `4a929def-e347-45b4-b26d-4325a3115859`, lost ACUs `d6a699b7-99bc-4a7f-b128-15e1e289a7b3`, built air `3ebb0c4d-5e92-4446-bf52-d17ba9c5cd3c`, lost air `225e9b2e-ae09-4ae1-a198-eca8780b0fcd`, built land `ea123d7f-bb2e-4a71-bd31-88859f0c3c00`, lost land `a1a3fd33-abe2-4e56-800a-b72f4c925825`, built naval `b5265b42-1747-4ba1-936c-292202637ce6`, lost naval `3a7b3667-0f79-4ac7-be63-ba841fd5ef05`, seconds played `cc791f00-343c-48d4-b5b3-8900b83209c0`, built tech 1 `a8ee4f40-1e30-447b-bc2c-b03065219795`, lost tech 1 `3dd3ed78-ce78-4006-81fd-10926738fbf3`, built tech 2 `89d4f391-ed2d-4beb-a1ca-6b93db623c04`, lost tech 2 `aebd750b-770b-4869-8e37-4d4cfdc480d0`, built tech 3 `92617974-8c1f-494d-ab86-65c2a95d1486`, lost tech 3 `7f15c2be-80b7-4573-8f41-135f84773e0f`, built experimentals `ed9fd79d-5ec7-4243-9ccf-f18c4f5baef1`, lost experimentals `701ca426-0943-4931-85af-6a08d36d9aaa`, built engineers `60bb1fc0-601b-45cd-bd26-83b1a1ac979b`, lost engineers `e8e99a68-de1b-4676-860d-056ad2207119`, Aeon plays `96ccc66a-c5a0-4f48-acaa-888b00778b57`, Aeon wins `a6b51c26-64e6-4e7a-bda7-ea1cfe771ebb`, Cybran plays `ad193982-e7ca-465c-80b0-5493f9739559`, Cybran wins `56b06197-1890-42d0-8b59-25e1add8dc9a`, UEF plays `1b900d26-90d2-43d0-a64e-ed90b74c3704`, UEF wins `7be6fdc5-7867-4467-98ce-f7244a66625a`, Seraphim plays `fefcb392-848f-4836-9683-300b283bc308`, Seraphim wins `15b6c19a-6084-4e82-ada9-6c30e282191f`.
- `achievement`: `name`, `description` (localised by the API from `Accept-Language`), `type` (`STANDARD`, `INCREMENTAL`), `totalSteps`, `revealedIconUrl`, `unlockedIconUrl`, `initialState`, `experiencePoints`, `order`, `unlockersCount`, `unlockersPercent`, `unlockersMinDuration`, `unlockersAvgDuration`, `unlockersMaxDuration`. `playerAchievement`: `currentSteps`, `state` (`REVEALED`, `UNLOCKED`, `HIDDEN`), `createTime`, `updateTime` [`player`, `achievement`].
- `league`: `technicalName`, `enabled`, `nameKey`, `descriptionKey`, `imageUrl`, `mediumImageUrl`, `smallImageUrl`. `leagueSeason`: `placementGames`, `placementGamesReturningPlayer`, `seasonNumber`, `nameKey`, `startDate`, `endDate` [`league`, `leaderboard` → type `leagueLeaderboard`: `technicalName`]. `leagueSeasonDivision`: `divisionIndex`, `nameKey`, `descriptionKey` [`leagueSeason`]. `leagueSeasonDivisionSubdivision`: `subdivisionIndex`, `nameKey`, `descriptionKey`, `minRating`, `maxRating`, `highestScore`, `imageUrl`, `mediumImageUrl`, `smallImageUrl` [`leagueSeasonDivision`]. `leagueSeasonScore`: `loginId`, `score`, `gameCount`, `returningPlayer` [`leagueSeason`, `leagueSeasonDivisionSubdivision`]. `leagueScoreJournal`: `gameId`, `loginId`, `scoreBefore`, `scoreAfter`, `gameCount` [`leagueSeason`, `leagueSeasonDivisionSubdivisionBefore`, `leagueSeasonDivisionSubdivisionAfter`]. Division and subdivision names come from their `nameKey` (last segment, capitalised; subdivisions as roman numerals from `subdivisionIndex` when the key gives nothing better); verify the keys live and keep a mapping table for the known ones.

The code lives in `Services/Players/` of the Viewer: `IPlayersApi`, the requests in `PlayerRequests`, the
reading in `PlayerMappers`, `FafPlayersApi` (the real API), `SamplePlayersApi` (embedded fixtures) and
`Statistics/`. Every name that could not be checked live carries an `// unverified` comment there: the
avatar include, the `faction`, `ratingChanges` and `playerStats.player.login` filters, custom games on
the `global` board, the opponents include on a journal page of 1,000, the head-to-head predicates,
the translation keys of divisions (`LeagueNames`), the `rating` attribute and `page[size]` limits.

The API's maximum page size and the exact spelling of a few fields are unverified: the first agent
that gets a token in a browser checks them (see Tooling) and corrects this file.

## Stories

- **P-1 Players section.** A "Players" item in the header (both menus), the landing page
  `players` with a search for a player by current or former name (results link to profiles) and
  cards to Rankings, Leagues and Compare; fixed link-preview cards on the server for the four
  pages and a generic card for `players/{id}`; a "Players tab" section in the Viewer's AGENTS.md.
- **P-2 Profile overview.** Name, clan tag and name, player id, member since, former names;
  one card per leaderboard the player has a rating on (rating, μ, σ, games, win share, change over
  30 days, league entry of the active season); the rating chart of one board over the last year
  with a hover tooltip; totals from the lifetime counters; the last games; achievements summary.
- **P-3 Rating history tab.** Period and board filters; tiles (current, peak, games and record,
  average change, biggest gain and loss); the rating line with a σ panel on the same time axis;
  the histogram of rating change per game; one small chart per board; the journal table with
  pages and a CSV download.
- **P-4 Matches tab.** One filter row (period, mode, faction, result, map, with or against);
  tiles (games, win share, average length, average rating change, most played map); win share by
  faction, by game length, by map (top 8), and the weekday × time-of-day heatmap; the games table
  with sorting and pages; every game links to its replay page.
- **P-5 Army and factions tab.** Games and wins per faction (stacked won and lost), custom against
  matchmaker, time in game, ACUs lost, units built and lost by category and by tech level,
  achievements with rarity and a state filter.
- **P-6 Rankings.** Board tabs, "settled only" and "played in" filters, the table (rank, player
  with clan tag, rating, σ, games, win share, league) in pages of 50 with the signed-in player's row
  pinned, "Jump to my rank", the "your position" card and the rating histogram with the player's bin.
- **P-7 Leagues.** League and season pickers; the signed-in player's entry (division, score meter,
  games, promotions this season); players per subdivision; the season journey step chart from the
  league journal; the standings of one subdivision with names.
- **P-8 Compare.** Two players by name (resolved to ids), dumbbells per board, a facts table with
  the better value bold, both rating lines on one scale with head-to-head games marked, faction
  split bars, and the head-to-head record and list.
- **P-9 Foundation.** Models and API methods for every entity above behind an `IPlayersApi`
  interface, a sample implementation fed by recorded JSON for development without a token, pure
  statistics functions, a Viewer test project, chart components and page chrome (player header,
  tabs, stat tiles, login gate), a development-only gallery page showing every chart component.
- **P-10 QA and docs.** A walkthrough of every page in both colour modes, at 1440 and 390 px wide,
  signed out and signed in, with shared links reopened in a private window; AGENTS.md, README and
  the home page card.

## Decisions (made by the main session; the owner can overturn any of them)

1. Profiles are addressed by id (`players/2049311`), never by name.
2. Every Players page is behind the login gate; nothing is fetched anonymously, the server never
   calls the API for player cards.
3. Distributions (rating histogram, players per subdivision) use count requests per bin, cached
   per board or season for the session, instead of downloading every rating.
4. Series colours: the player's own series in `primary`; two players in `viz-1` and `viz-2`;
   factions by their swatch (`Factions.Swatch`), always with the faction's name or icon next to
   the mark; magnitudes in one hue; good and bad changes in `viz-3` and `viz-8` with a sign or a
   word, never colour alone. Text never wears a series colour.
5. Times are shown in the viewer's local time zone; the heatmap too.
6. A Viewer test project (`tests/FAForever.Vault.Viewer.Tests`, MSTest, no bUnit) covers the
   JSON:API mapping with recorded fixtures and the statistics functions.
7. Development without a token: `ASPNETCORE_ENVIRONMENT=Sample` on the server makes Blazor load
   `wwwroot/appsettings.Sample.json`, which sets `Players:SampleData` to `true`, and the app then
   registers the sample `IPlayersApi` (the fixtures as embedded resources). Nothing in the URL,
   nothing in production.
8. Head to head is one request with both predicates; if the API does not support that, both
   players' match lists of the period are intersected in the browser.
9. Lists come in pages of 25 (matches) and 50 (rankings, standings); charts of a tab use the whole
   filtered period, fetched in pages of 1,000 behind a progress line.
10. Nothing new in the Server beyond fixed link-preview cards.

## Waves

Numbered after the map editor's waves (the last was 30). Each agent works in its own worktree under
`D:\wt` on a branch from `feature/players`; the main session merges into `feature/players` and
reports after each wave. Agents run on Sonnet. Build and test with `--artifacts-path D:\wt\art-<id>`.

| Wave | Agent | Branch, worktree, port | Stories |
|---|---|---|---|
| 31 | A data | `wave31/api`, `D:\wt\31a`, 5390 | P-9 data: models, `IPlayersApi`, real and sample implementations, fixtures, statistics, test project, sample environment |
| 31 | B shell | `wave31/shell`, `D:\wt\31b`, 5391 | P-9 UI: chart components, player header, tabs, tiles, login gate, gallery page; P-1 without the search (routes, nav, landing cards, server cards, AGENTS.md section) |
| 32 | A profile | `wave32/profile`, 5392 | P-2, P-3 |
| 32 | B matches | `wave32/matches`, 5393 | P-4, P-5 |
| 32 | C rankings | `wave32/rankings`, 5394 | P-6, P-7 |
| 32 | D compare | `wave32/compare`, 5395 | P-8, the landing page search of P-1 |
| 33 | QA | `wave33/qa`, 5396 | P-10 walkthrough |
| 34 | fixes | as needed | P-10 fixes and docs |

## Tooling

- Run the app on sample data (no token, no login): from the repository folder,
  `ASPNETCORE_ENVIRONMENT=Sample ASPNETCORE_URLS=http://127.0.0.1:5390 dotnet run --project src/FAForever.Vault.Server --artifacts-path D:\wtrt-<id> --no-launch-profile`
  (PowerShell: set `$env:ASPNETCORE_ENVIRONMENT = 'Sample'` and `$env:ASPNETCORE_URLS` first). The
  Server serves the Viewer's files in that environment (`UseStaticWebAssets`), Blazor loads
  `wwwroot/appsettings.Sample.json` (the response header `Blazor-Environment: Sample`), and the app is
  signed in as the sample player 2049311 "Ashgrove" (`AuthService.InitializeAsync`). Rebuild and restart
  after a change; stop the server before building to the same artifacts path.
- Agents cannot sign in: the development OAuth client only redirects to `127.0.0.1:5080`, which is
  the owner's server. Pages are checked with the sample API (decision 7) on the agent's own port.
  The owner can give one Playwright profile a real session (`--user-data-dir=.playwright-mcp/profile`
  and a login on 5080 once); the QA wave uses it when it exists.
- Every agent reports a "Tooling" section: whether it could test its work (tests, the sample API,
  Playwright) and what to add. The wave reports collect them.
- A worktree has no `tools/tailwindcss.exe`; the main session copies it in. The committed
  `wwwroot/css/app.css` must be regenerated (`--minify`) whenever new classes are used.
- `dotnet watch` on 5080 belongs to the owner: never build in `D:\cs-development\scfa-cs-replay`.
