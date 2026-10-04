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

## Blueprint ids, unit names and factions

- `BlueprintIds` decodes the id convention (`ueb0101` = [prefix u][faction e][layer b][number 0101]):
  `GetFaction`, `GetLayer`, `GetTechLevel`. Mod units need not follow it, so every result is nullable.
- `Faction` holds the lobby's faction indices (1 = UEF … 4 = Seraphim, 5 = random); the replay's
  lobby data and the FAF API use the same numbering.
- Unit display names ← the `Description` fields of `units/*_unit.bp`, generated into `UnitNames.g.cs`
  by `tools/generate-unit-names.ps1` (`UnitNames.GetOrNull(blueprintId)`; never edit the generated
  file). Re-run it after game updates.
