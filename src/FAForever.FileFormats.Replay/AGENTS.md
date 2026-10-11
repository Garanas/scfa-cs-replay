# Agent guide: FAForever.FileFormats.Replay (the parser)

The core replay parser: the crown jewel. Change it with care: it is benchmarked and heavily
tested. The repository-wide guide is [`../../AGENTS.md`](../../AGENTS.md); how the Viewer drives the
parser in the browser is in [`../FAForever.Vault.Viewer/AGENTS.md`](../FAForever.Vault.Viewer/AGENTS.md).
Lua values (`LuaData`), factions, blueprint ids and unit names come from the Lua and Blueprints
libraries: see [`../FAForever.FileFormats.Blueprints/AGENTS.md`](../FAForever.FileFormats.Blueprints/AGENTS.md).

## Tests and benchmarks

- Parser changes must keep `ReplayFingerprintTest` green: it pins the complete parsed output of every
  replay asset. When and how to update its hashes: [`../../tests/AGENTS.md`](../../tests/AGENTS.md).
- Performance work is measured with the benchmarks and logged in their `RESULTS.md`; how to run and
  log them: [`../../benchmarks/AGENTS.md`](../../benchmarks/AGENTS.md).

## The replay model

- `.fafreplay` = JSON metadata line + compressed body (zstd, or legacy base64+zlib);
  `.scfareplay` = raw body; construct `ReplayLoadingStage.Decompressed(stream, null)` directly.
- `ProcessReplayStage(WithMetadata)` disposes the input stream; don't reuse it.
- Every input carries `Tick`: the number of Advance messages before it, ten per second. Strictly it
  is the engine's **beat**: beats go on while the game is paused, the simulation's tick does not, so
  after a pause `Tick` (and every timestamp derived from it) runs a few seconds ahead of game time.
- Command selections (`CommandUnits.EntityIds`) carry the entity ids the order went to. An entity id
  is `[31..28] family, [27..20] index, [19..0] serial` (`ReplayEntityId`); for units the family is 0
  and the index is the army, so a unit id is `(army index << 20) | serial`. Serial 0 is the army's
  first unit, its commander (ACU). **Serials are reused**: a new unit takes the lowest free serial,
  and a destroyed unit's serial becomes free about 100 beats (10 s) later, so one id can name
  several units in a long game and serials do not follow the order in which units appeared.
  Selections hold only units. What kind of unit an id is, the replay does not say;
  `ReplaySemantics.GetEntities` guesses it from the orders (`ReplayEntityKind`).
- Entity targets (`CommandTarget.Entity`) carry the family too: a unit (own or allied: guard,
  repair, load), a prop (index 0xFF: reclaim) or a **recon blip** (family 3, index = the army of the
  unit it shows: attacks, captures, overcharges on enemies). A blip has its own id; the replay cannot
  map it to the enemy unit's id. Measured: of 663 k entity targets, 309 k units, 173 k props, 182 k blips.
- Command ids (`CommandData.Identifier`) are `(source << 24) | serial`, from a per-source pool
  with the same reuse delay: an id repeats in most long games, about 10 s or more after its
  command ended. Match a later message to the latest order with that id (as `GetQueueChanges` does).
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
  `Raw` holds the full Lua table, e.g. `OwnerID`, the FAF player id). `Armies[i].SourceId` links an
  army to `Header.Clients`; it is null for AI and civilian armies. Clients without an army are
  observers. `Header.Clients` are the command sources: the players in army order, then the
  observers, each with a name and `PauseAllowance` (the engine's `mTimeouts`: 3 for FAF players,
  0 for observers, -1 unlimited in single player). A source id is not unique per army: in
  `gzip/20802415.fafreplay` two armies list source 0 and the header has one client.
  Game options live in `Header.Scenario.Options`. What a replay does **not** know is who won:
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
`ReplayScenarioOptions` and the `GetFlexibleBool` reader). Unit display names live in the Blueprints
library (`UnitNames`). Re-check all of these after game updates.

## What the engine does (faf-re)

[Draiget/faf-re](https://github.com/Draiget/faf-re) is a C++20 reconstruction of the retail Forged
Alliance engine (v1.50.3764) from its binary, with the address of every function. The statements
below are based on its commit **`2ba55b2`** (2026-10-08); paths are under `src/sdk/` of that
repository, line numbers hold at that commit. "Measured" means checked on 685 real replays (the
test assets and the owner's FAF replays of 2024–2026) with an independent decoder written to the
engine's layout; 663 of them decode completely (two hold only a header, one sits in FAF's own
`corrupt/` folder).

### The file

- **Header**: written by `VCR_CreateReplay`, read by `VCR_SetupReplaySession`
  (`moho/misc/SessionStartup.cpp:344-468` and `:479-590`). Strings carry their closing zero
  (`gpg/core/streams/Stream.cpp`). In order: the game version (`"Supreme Commander v1.50.%4i"`; FAF
  writes its own build number, the Steam version `v1.60.   6`); `"\r\n"`; the banner
  `"Replay v1.9\r\n"` **without** a zero (13 bytes the engine compares) directly followed by the map
  path, which is the **`.scmap` path** (`mMapName`, e.g. `/maps/SCMP_009/SCMP_009.scmap`; measured
  685/685; `ReplayHeader.MapPath`, `ReplayLoader.TryReadMapPath`); `"\r\n\x1A"` (a line break and
  Ctrl-Z, the DOS end-of-text mark; measured 685/685); the mods and the scenario, each an int32 length
  plus a Lua byte stream; a byte count of command sources, each a name and an int32 `mTimeouts`
  (`moho/sim/SSTICommandSource.h`; our `ReplaySource.PauseAllowance`); the cheats byte; a byte count
  of armies, each an int32 length plus the army's lobby options as Lua, then **the indices of the
  sources that control the army, one byte each, closed by 0xFF**; the uint32 random seed.
- **Sources** come from the lobby's launch (`moho/net/CLobby.cpp:3290-3375`): the human players in
  army order, then observers; AI and civilian armies (`ExtraArmies`) get none. A player's
  `mTimeouts` is the lobby option `Timeouts` (3 on FAF), an observer's 0 in multiplayer and -1 in
  single player. `Sim::RequestPause` refuses a pause at 0, spends one above 0 and never runs out
  below 0 (`moho/sim/Sim.cpp:7939-7970`). It never equals the army's `OwnerID` (measured 0/3,910).
- **Body**: a stream of framed messages, `uint8 type, uint16 length of the whole message (these
  three bytes included), content` (`CMessage`, `moho/net/CMessage.h:29-31, 100-114`). The recorder
  appends every command message it receives verbatim (`CDecoder::ReceiveMessage`,
  `moho/misc/CDecoder.cpp:133-143`). Types 0 to 23 (`ECmdStreamOp`, `moho/net/ECmdStreamOp.h`;
  same numbering as `ReplayInputType`) are decoded by `CDecoder::DecodeMessage`
  (`moho/misc/CDecoder.cpp:151-238`), which throws on any other type; the writers are the
  `CMarshaller` methods in `moho/net/CClientManagerImpl.cpp:149-560`, the effects the handlers in
  `moho/sim/Sim.cpp`.
- **Playback** (`CReplayClient::Start`, `moho/net/CReplayClient.cpp:212-328`) drops messages other
  than Advance from sources the header does not list, and adds `EndGame` and `Advance(1)` at the end
  of the stream. Measured: 238 of 664 replays have no EndGame (the recorder left early); no Advance
  follows an EndGame in any replay.

### The messages whose layout is easy to get wrong

`CDecoder.cpp` has one `Decode<Name>` function per type; these are the ones that need care:

| Type | Content | Notes |
|---|---|---|
| 3 VerifyChecksum | MD5 (16 bytes), int32 beat | One per client every 50 beats (`sim_ChecksumPeriod`), arriving 0–19 beats later. The digest covers economy totals, dirty entities (id, health, blueprint, transform, velocity), the random state, command creations (`Sim::UpdateChecksum`, `Sim.cpp:5951-6074`). We XOR the halves and compare clients with each other. |
| 7 CreateUnit | uint8 army, string blueprint, float x, z, heading | Cheats only (`Sim.cpp:8283-8320`). |
| 8 CreateProp | string blueprint, float **x, y, z** | Cheats only (`Sim.cpp:8332-8343`). |
| 10 WarpEntity | int32 entity, **VTransform**: quaternion w, x, y, z, then position x, y, z (28 bytes) | `DecodeWarpEntity`. Cheats only (`Sim.cpp:8364-8378`). In 22 measured replays, all sandbox games, e.g. `gzip/20802415.fafreplay`. |
| 11 ProcessInfoPair | int32 **entity id**, string key, string value | One message per unit; keys in `Sim.cpp:8384-8505` (see the record's doc). |
| 12/13 IssueCommand / IssueFactoryCommand | selection, command data, uint8 clear queue | See below. |
| 18 **SetCommandCells** | int32 command id, int32 count, count × (int16 x, int16 z), float x, y, z | `DecodeSetCommandCells`; replaces the command's cells and sets a ground target (`Sim.cpp:8682-8706`). No recovered UI or Lua sends it; never measured. |
| 20 DebugCommand | string, float x, y, z, uint8 focus army, selection | Seen: SallyShears, AI_RunOpponentAI, dbg, NoDamage, KillAll, … |
| 21 ExecuteLuaInSim | string **function name**, **Lua argument** | `DecodeExecuteLuaInSim`; the UI global `ExecLuaInSim(name, args)` (`Sim.cpp:15077-15098`, used by `lua/debug/ArmyStats.lua`). Recorded whatever the cheat setting; run only with cheats (`Sim.cpp:8772-8790`). Never measured. |
| 22 LuaSimCallback | string name, Lua argument, selection | Calls `DoCallback` of `/lua/SimCallbacks.lua` (`Sim.cpp:8852-8900`). |
| 23 EndGame | — | The next beat sets game over; the recorder stops (`CDecoder.cpp:627-631`). |

**Command data** (`CDecoder::DecodeCommandData`, `CDecoder.cpp:685-734`; struct
`SSTICommandIssueData`, `moho/command/SSTICommandIssueData.h:103-117`), in order, with the faf-re
field name and ours: int32 command id (`nextCommandId`, `Identifier`); int32 `unk04`
= `CoordinatingCommandId`, -1 or the id of an earlier attack of the same source that this one
coordinates with ("Coordinated Attack!", `moho/sim/CWldSession.cpp:11620-11630`,
`moho/unit/CUnitCommand.cpp:896-924`; measured 792 of 4.27 M orders, all attacks); uint8 type
(< 40, `EUnitCommandType`, identical to `CommandType`); int32 `mIndex` = `InsertBeforeCommandId`,
-1 to append (`Sim.cpp:3406-3447`; measured 7,433, mostly factory queues); target and **second
target** (`mTarget2`), each a type byte 0 none / 1 entity (int32) / 2 ground (3 floats), any other
type rejected (`DecodeTarget`; the second was type 0 in every measured order); int32 `unk38` =
formation script index, -1 for none, else a quaternion w, x, y, z (`mOri`; norm 1 in 2.0 M of
2.0 M) and a float `unk4C` = scale (always 1); string blueprint id; the **cell list**
(`DecodeCells`: int32 count, count × int16 x, int16 z; always empty); int32 `unk70` = `MaxCount` and
int32 `unk74` = `Count` (`moho/command/SSTICommandVariableData.cpp:166-174`; always 1 and 1); the Lua
argument (`mObject`). IssueCommand adds the clear-queue byte: 1 replaces the queue, 0 appends, the
decoder throws on 2 or more (`CDecoder.cpp:454-462`). The sim silently drops units the source may
not control (`OkayToMessWith`, `Sim.cpp:7741-7808`) and non-clearing orders to a unit with more
than 500 queued commands (`Sim.cpp:3418-3420`).

**Lua byte stream** (`lua/LuaObject.cpp:21039-21120` read, `:21425-21500` write): tags 0 number
(float32), 1 string, 2 nil, 3 boolean (a byte, non-zero is true; the writer emits 1 and 0),
4 table … 5 end. The engine allows any non-nil table key; replays hold only string and number keys
(measured 9.83 M keys), which is all `LuaDataLoader` accepts.

### Ids, ticks and checksums

- **Entity ids** (`moho/entity/EntityId.h:8-44`): `[31..28] family, [27..20] index, [19..0] serial`.
  The allocators name the families: 0 unit (`armyIndex << 20`, `moho/unit/core/Unit.cpp:12889-12897`),
  1 projectile (`(army | 0x100) << 20`, `moho/projectile/Projectile.cpp:401`), 2 prop (index 0xFF,
  `moho/entity/Prop.cpp:52-56`), 3 recon blip (`(army | 0x300) << 20`, `moho/sim/ReconBlip.cpp:95,
  132-142`), 4 shield (`moho/entity/Shield.cpp:43, 74-78`), 5 script entity
  (`moho/entity/Entity.cpp:4667`). `ReplayEntityId` and `ReplayEntityFamily` mirror this.
- **Reuse**: `EntityDB::DoReserveId` takes the lowest released serial before a new one
  (`moho/entity/EntityDb.cpp:1532-1550`); a destroyed entity releases its id (`Entity.cpp:1949-1952`)
  into a 100-slot history ring (`IdPool::QueueReleasedLowId`, `moho/sim/IdPool.cpp:255-259`) that
  `EntityDB::Purge` advances once per beat (`EntityDb.cpp:1330-1348`, `Sim.cpp:5686`), so a serial
  comes back about 100 beats after its entity died. Command ids: `(source << 24) | serial` from a
  per-source pool with the same ring (`Sim.cpp:2070-2097, 2662, 2695`); the sim rejects an id whose
  top byte is not the current source or that exists (`Sim.cpp:8009-8040`) and finds a command by id
  alone (`Sim.cpp:8631-8650`). Measured: a command id repeats in 486 of 664 replays, the gap 100
  beats or more in all but 21 cases.
- **Beats and ticks**: an Advance advances the beat; `Sim::AdvanceBeat` advances the sim tick only
  when the game is not over and not paused, or on a single step (`Sim.cpp:10567, 10612-10613`).
  Ten ticks a second (`moho/sim/CEconomy.cpp:234`). Measured: 166 of 664 replays have beats inside a
  pause, 3,930 in all, up to about 160 in one pause.

### What we rely on

- Every message decodes to exactly its framed length. The loader checks it: a message that does not
  (or of a type above 23, or cut off by the end of the stream) becomes a `ReplayInput.Error` (or
  `ReplayInput.Unknown`) with its content, has no other effect, and the loader reads on after it
  (`EveryMessageDecodesToItsLengthTest` pins that no asset has one). Advance, SetCommandSource and
  VerifyChecksum have a fixed size and are checked before they are read, so they stay out of the
  exception handling (see the benchmarks' `RESULTS.md`, step 7).
- The source of a message is the last SetCommandSource; a unit id's index is an army of that source
  except in cheat games (measured: the 22,600 exceptions in 32.3 M ids are all in cheat games).
- An army's `SourceId` is its first listed source; the lobby lists at most one per army
  (measured 5,492 armies: 3,910 with one, 1,582 with none).

### Where not to trust faf-re

- It is the **retail** engine. FAF's executable is patched, and faf-re notes that
  `CDecoder::DecodeMessage` is redirected to `0x0128F810` and lists a second decoder at `0x0128CC00`
  in FAF's patch area that accepts only Advance and SetCommandSource (`moho/misc/CDecoder.h:71-79`,
  `CDecoder.cpp:254-281`); what FAF changed there is not recovered. In practice every measured FAF
  replay decodes with the retail layout and holds only types 0 to 23.
- Its header writer writes `"\r\n"` after the map path; every real replay has `"\r\n\x1A"`. Either
  FAF's executable differs there or the recovery is off; the reader skips the string either way.
- `moho/entity/EntityId.h` labels family 3 `ShieldLike` ("still being refined") and calls the middle
  byte a "source index"; its own allocators (`ReconBlip.cpp`, `Shield.cpp`, `Unit.cpp`) make 3 a
  recon blip, 4 a shield and the index the army.
- Field names such as `unk04`, `unk38`, `unk4C`, `unk70`, `unk74` and `mIndex` are placeholders.
  The meanings above come from where they are used (`CUnitCommand.cpp`, `Sim.cpp`,
  `CWldSession.cpp`, `SSTICommandVariableData.cpp`) and agree with the measured values.
- `Sim::AdvanceBeat` is marked a "partial high-fidelity lift" (`Sim.cpp:10563-10565`), the `EntId`
  typedef "an interim model" (`moho/sim/SSTITarget.h:276-279`) and `CDecoder::DecodeLuaObject` a
  "synthetic host-build helper" (`CDecoder.cpp:807-816`).
- The replay path carries `TEMPORARY PROBE -- replay beat-pipeline triage` blocks that write a
  `faf_diag.log` (`CDecoder.cpp:435` in `DecodeIssueCommand`, `:603` in `DecodeDebugCommand`;
  `CReplayClient.cpp:214, 248, 266`; `Sim.cpp:8521, 10328, 10578`) and a comment that playback
  deadlocks on beat 0 (`CReplayClient.cpp:158-163`): replay playback in faf-re was being debugged at
  this commit. Read those functions around the probes.
- What goes into the checksum is reconstructed from logging code; the exact byte ranges (such as the
  size of the economy struct) are not verified.

### Other tooling

- faf-java-commons (`LoadUtils.parseLua`) reads Lua booleans inverted (`== 0` is true); the engine
  writes 1 for true (see "The replay model").
- The FAF server writes `"mapname":"None"` in the metadata of games on generated maps; the map
  path of the header names them.
- A reader that steps through the body by decoding alone, without the framed length, loses its
  place on the first message it reads in the wrong layout: WarpEntity read as an id and three
  floats leaves 16 bytes that are taken for the next message, and the rest of the file fails.

### Open items

- **One entity id, several units.** `ReplaySemantics.GetEntities` and its kind guess merge the
  orders of every unit that held an id over the game, and `ReplayEntityKind.Commander` holds only
  until the commander dies. The replay does not say when a unit died, so splitting an id into lives
  needs a heuristic (a gap of more than 10 s followed by orders that do not fit the earlier kind);
  it would change what the Build order tab shows, so it waits for a decision.
- **Game time.** `Tick` counts beats. Converting to the sim tick means tracking pauses with the
  engine's rules (RequestPause honoured when not paused and the source's allowance is not 0, any
  source resumes, a source that leaves resumes its own pause, SingleStep advances one tick;
  `Sim.cpp:7828-7838, 7939-7984`). That would shift the timestamps the Viewer shows after a pause.
- **Queue changes by command id alone.** `GetQueueChanges` matches on (source, command id); the
  engine looks a command up by id alone, which would resolve the few changes sent by another source
  (measured 36, in cheat or allied games).
- **Checksum detail.** The engine records which source disagreed at which beat (`SDesyncInfo`,
  `Sim.cpp:7873-7925`); we keep only whether the replay stayed in sync.
