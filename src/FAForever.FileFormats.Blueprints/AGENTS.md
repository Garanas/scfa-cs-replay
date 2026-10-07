# Agent guide: FAForever.FileFormats.Blueprints and FAForever.FileFormats.Lua

The game's data files: blueprints (`.bp`) here, and the Lua they are written in next door in
`../FAForever.FileFormats.Lua`. The repository-wide guide is [`../../AGENTS.md`](../../AGENTS.md); the
replay parser, which builds on both, has its own in
[`../FAForever.FileFormats.Replay/AGENTS.md`](../FAForever.FileFormats.Replay/AGENTS.md).

Dependencies point one way: Replay → Blueprints → Lua. Neither library may reference the replay
parser. Both are pure managed code without file access, so they run in WebAssembly; callers supply
the text.

## Lua (`FAForever.FileFormats.Lua`)

- `LuaData` (`Nil`, `Bool`, `Number`, `String`, `Table`) is the value model shared by replays and
  blueprints. Table keys are strings: positional entries get "1", "2", ... (like tables read from a
  replay), a boolean key becomes "true"/"false". `LuaDataFormatter` prints values as Lua.
- `LuaSourceParser` evaluates the data-only Lua subset (tables, constant expressions, named calls to
  functions the caller supplies; no function definitions or control flow) and throws
  `LuaSyntaxException` with a line number.
- `LuaTableReader` reads typed fields from a table (`Number`, `String`, `Section`, `List`,
  `Dictionary`, ...); a missing or mistyped field reads as absent. The blueprint and map records
  are built with it, each by its own `Read` method or parser.
- The replay's binary encoding of Lua values (`LuaDataLoader`, `LuaDataType`) belongs to the replay
  format and stays in the replay parser.

## Blueprints

`BlueprintParser.Parse(text, "/units/uel0101/uel0101_unit.bp")` mirrors `lua/system/Blueprints.lua`
(kinds, blueprint ids) on top of `LuaSourceParser`. It returns typed records (`BlueprintUnit` with
`BlueprintUnitEconomy`, `BlueprintWeapon`, …; `BlueprintProjectile`, `BlueprintProp`, `BlueprintMesh`,
`BlueprintEmitter`, `BlueprintTrailEmitter`, `BlueprintBeam`), each read by its own `Read` method through
`LuaTableReader`. They hold a hand-picked subset of fields, named after the file's keys (field
meanings: `engine/Core/Blueprints/*.lua` in the FA repo); every record keeps its full table in `Raw`.
Data is as written in the file, before the game's post-processing (mod merges, `ModBlueprints`); most
units leave out `General.TechLevel`, so use `BlueprintUnit.TechLevel` (from the categories). Every `.bp`
in the FA repo parses; tests use the copies in `tests/FAForever.FileFormats.Blueprints.Tests/assets/blueprints`.

## Unit data (the unit cards)

The browser does not parse blueprints: `tools/generate-unit-data.cs` (a file-based app, `dotnet run
tools/generate-unit-data.cs -- <fa checkout>`) parses `units/*_unit.bp` of one FA release and adds it
to `src/FAForever.Vault.Viewer/wwwroot/data/units/`, which the unit cards and the unit database read.
Every release from 3801 (there is no 3800 tag) is there; the oldest version is also
`UnitDatabase.FirstVersion` in the Viewer (the Units page shows it), so change both together.

- `UnitSummary` is what a card shows (name, faction, tech, motion type, categories, cost, build power,
  health, shield, intel ranges, weapons with damage, salvo, range and rate of fire); `UnitSummary.From`
  maps a `BlueprintUnit`. Nothing is computed (no DPS), but values are **evened out** so that versions
  only differ where the meaning does: blueprints get rewritten (3810 dropped `RegenRate = 0` from 549
  units, others write `10/60` as `0.1667` or reorder categories), so numbers are rounded to 4 decimals,
  0 reads as not set where it means "none", categories are sorted, and the rate of fire is the one the
  game fires at: 10 / whole ticks (`UnitSummaryWeapon.TickRate`, after `lua/system/blueprints-units.lua`),
  since 3810 rewrote rates such as 0.15 as `10/67`. Without that, the history and
  the "changed" filter would mostly show rewrites.
- A data file is `{"units":[...]}` with **one unit per line**, sorted by id, camel case, nulls left
  out; `UnitData` is exactly that, the units and nothing else. Which game versions a file belongs to
  is only the index's business, so neither the file nor `UnitData` carries a version. JSON goes
  through the source-generated `UnitDataJsonContext` (no reflection, safe under trimming in WebAssembly).
- `UnitDataIndex` is `units/index.json`: an entry per game version, one per line, newest first:
  `"3838":{"file":"3837.json","unitCount":606,"reusedFrom":3837,"changes":{"previous":3837,"changed":[],
  "added":[],"removed":[]},"commit":"…","released":"2026-08-25","generated":"2026-10-05"}`.
  - **A release that changed no unit reuses a file**: the generator compares the new units with every
    file a version owns (`UnitData.HasSameUnits`) and only writes `<version>.json` when none matches, so
    a file is named after the first version with its units; `reusedFrom` names that version.
  - `changes` lists the ids changed, added and removed since the previous version (`UnitData.Compare`);
    the Units page filters on it, and the generator prints it for the update workflow's pull request
    (a new file has no useful diff).
  - `commit` and `released` (the release tag's commit and its date) come from the generator's options,
    which the backfill script and the workflow fill in; `generated` is the day of the run, so it only
    changes when that version is regenerated. Nothing else in the files depends on when they were made.
  - `Resolve` picks the data for a game: its own version, else the newest older one, else the oldest
    (3800 and before). The Viewer's `UnitDatabase.LoadAsync(version)` returns that resolved version
    next to the data (`UnitDataVersion`); versions that share a file share one `UnitData`.
- `UnitBuildTree` mirrors who builds what: a builder can build every unit that has all categories of
  one of its `Economy.BuildableCategory` expressions (space separated; a unit's own lower case id
  counts as a category, so `"uab3101"` names a unit), commanders also what their enhancements add
  (`BuildableCategoryAdds`). A unit is `Buildable` when a commander reaches it through builds and
  `UpgradesTo`; that excludes campaign, civilian and helper units (404 of 606 units in 3839).
  `UnitData.From` fills `Buildable` and `Builds` in; `UnitData.GetBuilders` is the inverse.
- The game version is the release tag, which is also the last number of a replay header's version.
  `mod_info.lua` usually says the same (`version`, read with `LuaSourceParser`), but not always:
  release 3805 still says 3804. So the backfill script and the workflow pass the tag (`--version`); the
  generator falls back to `mod_info.lua` without it and notes a mismatch.
- `.github/workflows/update-unit-data.yml` adds a new release and opens a pull request when FA
  publishes one (a `repository_dispatch` from the FA repository, by hand, or a daily check).
- **Adding a field** (a property on `UnitSummary` or `UnitSummaryWeapon`, its line in `From`, a test)
  changes every data file: regenerate them all from scratch with
  `pwsh tools/backfill-unit-data.ps1 -Source <fa clone>` (it extracts each release tag with an archive
  of `units/` and `mod_info.lua`, so the clone is left alone) and commit `data/units/`. The generator
  refuses to overwrite a file that other versions share.

## Blueprint ids, unit names and factions

- `BlueprintIds` decodes the id convention (`ueb0101` = [prefix u][faction e][layer b][number 0101]):
  `GetFaction`, `GetLayer`, `GetTechLevel`. Mod units need not follow it, so every result is nullable.
- `Faction` holds the lobby's faction indices (1 = UEF … 4 = Seraphim, 5 = random); the replay's
  lobby data and the FAF API use the same numbering.
- Unit display names ← the `Description` fields of `units/*_unit.bp`, generated into `UnitNames.g.cs`
  by `tools/generate-unit-names.ps1` (`UnitNames.GetOrNull(blueprintId)`; never edit the generated
  file). Re-run it after game updates.
