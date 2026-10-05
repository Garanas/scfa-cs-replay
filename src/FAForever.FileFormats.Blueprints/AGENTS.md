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
- The replay's binary encoding of Lua values (`LuaDataLoader`, `LuaDataType`) belongs to the replay
  format and stays in the replay parser.

## Blueprints

`BlueprintParser.Parse(text, "/units/uel0101/uel0101_unit.bp")` mirrors `lua/system/Blueprints.lua`
(kinds, blueprint ids) on top of `LuaSourceParser`. It returns typed records (`BlueprintUnit` with
`BlueprintUnitEconomy`, `BlueprintWeapon`, …; `BlueprintProjectile`, `BlueprintProp`, `BlueprintMesh`,
`BlueprintEmitter`, `BlueprintTrailEmitter`, `BlueprintBeam`), each read by its own `Read` method through
`BlueprintTableReader`. They hold a hand-picked subset of fields, named after the file's keys (field
meanings: `engine/Core/Blueprints/*.lua` in the FA repo); every record keeps its full table in `Raw`.
Data is as written in the file, before the game's post-processing (mod merges, `ModBlueprints`); most
units leave out `General.TechLevel`, so use `BlueprintUnit.TechLevel` (from the categories). Every `.bp`
in the FA repo parses; tests use the copies in `tests/FAForever.FileFormats.Blueprints.Tests/assets/blueprints`.

## Unit data (the unit cards)

The browser does not parse blueprints: `tools/generate-unit-data.cs` (a file-based app, `dotnet run
tools/generate-unit-data.cs -- <fa checkout>`) parses `units/*_unit.bp` of one FA release and adds it
to `src/FAForever.Vault.Viewer/wwwroot/data/units/`, which the unit cards and the unit database read.
Every release from 3801 (there is no 3800 tag) is there.

- `UnitSummary` is what a card shows (name, faction, tech, motion type, categories, cost, build power,
  health, shield, intel ranges, weapons with damage, salvo, range and rate of fire); `UnitSummary.From`
  maps a `BlueprintUnit`. Values are as written in the blueprint; nothing is computed (no DPS).
- `UnitData` is a data file: `{"gameVersion":3837,"units":[...]}` with **one unit per line**, sorted
  by id, camel case, nulls left out. JSON goes through the source-generated `UnitDataJsonContext` (no
  reflection, safe under trimming in WebAssembly).
- `UnitDataIndex` is `units/index.json`: the data file of every game version, one per line, newest
  first. **A release that changed no unit shares a file**: the generator compares the new units with
  every existing file (`UnitData.HasSameUnits`) and only writes `<version>.json` when none matches, so a
  file is named after the first version that has its units (`"3838": "3837.json"`). `Resolve` picks
  the data for a game: its own version, else the newest older one, else the oldest (3800 and before).
  `UnitData.WithGameVersion` relabels shared data with the version it stands for.
- The generator prints what changed since the previous version (`UnitData.Compare`: units changed,
  added, removed); the update workflow puts that in its pull request, since a new file has no diff.
- `UnitBuildTree` mirrors who builds what: a builder can build every unit that has all categories of
  one of its `Economy.BuildableCategory` expressions (space separated; a unit's own lower case id
  counts as a category, so `"uab3101"` names a unit), commanders also what their enhancements add
  (`BuildableCategoryAdds`). A unit is `Buildable` when a commander reaches it through builds and
  `UpgradesTo`; that excludes campaign, civilian and helper units (404 of 606 units in 3839).
  `UnitData.From` fills `Buildable` and `Builds` in; `UnitData.GetBuilders` is the inverse.
- The game version comes from `version` in the FA repository's `mod_info.lua` (read with
  `LuaSourceParser`); it is also the release tag and the last number of a replay header's version.
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
