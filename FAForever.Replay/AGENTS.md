# Agent guide — FAForever.Replay (the parser)

The core replay parser: the crown jewel. Change it with care — it is benchmarked and heavily
tested. The repository-wide guide is [`../AGENTS.md`](../AGENTS.md); how the Viewer drives the
parser in the browser is in [`../FAForever.Replay.Viewer/AGENTS.md`](../FAForever.Replay.Viewer/AGENTS.md).

## Tests and benchmarks

- Parser changes must keep `ReplayFingerprintTest` green: it pins a hash of the complete parsed output
  of every test asset, and checks that the staged API matches a one-shot load. Only update the expected
  hashes when an output change is intended.
- Benchmarks (`FAForever.Replay.Benchmark`, always `-c Release`): without arguments everything runs;
  `dotnet run -c Release -- --filter "*ParseBenchmark.Body*"` runs one phase. Phases: `DecompressBenchmark`
  (metadata + decompression), `ParseBenchmark.Header` / `.Body` (on pre-decompressed bytes, body via the
  staged API with the Viewer's batch size), and the end-to-end `FAForeverReplayBenchmark` /
  `SCFAReplayBenchmark`. Log results per optimisation step in `FAForever.Replay.Benchmark/RESULTS.md`.

## The replay model

- `.fafreplay` = JSON metadata line + compressed body (zstd, or legacy base64+zlib);
  `.scfareplay` = raw body — construct `ReplayLoadingStage.Decompressed(stream, null)` directly.
- `ProcessReplayStage(WithMetadata)` disposes the input stream; don't reuse it.
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
- End-of-game statistics (`lua/sim/score.lua`; the report the server uses for achievements, **not**
  the data of the in-game score screen, which the replay does not have) reach the replay because FAF hooks
  `GpgNetSend` (`lua/ui/globals/GpgNetSend.lua`) to log every call as a `ModeratorEvent` sim callback:
  `Message = "GpgNetSend with command 'JsonStats' and data '<json>,'"`, once per client at game end.
  Use `ReplaySemantics.GetGameStats` (null when absent or unparseable; never throws). The game's dkson
  writes every fractional number with a spurious `0` after the decimal point (`1415.1` → `1415.01`);
  `ReplayGameStatsReader` undoes that. Shown on the Statistics tab (`StatsPanel`).
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

## Game-data tables

Mirrored from the FA repo, with their lua sources: in-game army colours in `GameColors.cs` ←
`lua/GameColors.lua`. The parsed model resolves them: **use `ReplayPlayerOptions.Color`** (CSS hex)
rather than the raw `PlayerColor`/`ArmyColor` indices; `GameColors.BySource(header)`/`ByName(header)`
give lookup maps for inputs and chat. Lobby option keys/values ← `lua/ui/lobby/lobbyOptions.lua` (see
`ReplayScenarioOptions` and the `GetFlexibleBool` reader). Unit display names ← the `Description`
fields of `units/*_unit.bp`, generated into `UnitNames.g.cs` by `tools/generate-unit-names.ps1`
(`UnitNames.GetOrNull(blueprintId)`; never edit the generated file). Re-check all of these after
game updates.
