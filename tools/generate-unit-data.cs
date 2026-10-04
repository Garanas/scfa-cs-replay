#!/usr/bin/env dotnet
// Generates src/FAForever.Vault.Viewer/wwwroot/data/units.json (a UnitData file) from the unit
// blueprints of the FAF game repository (github.com/FAForever/fa): a summary of every unit for the
// unit cards. The game version comes from the repository's mod_info.lua, so point it at a checkout
// of a release tag. Output is deterministic (one unit per line, sorted by id), so the diff after a
// game update shows which units changed.
//
//   dotnet run tools/generate-unit-data.cs -- <fa checkout> [output file]
//
// A release without a full checkout: git -C <fa> archive refs/tags/3839 units mod_info.lua | tar -x -C <dir>

#:project ../src/FAForever.FileFormats.Blueprints/FAForever.FileFormats.Blueprints.csproj

using FAForever.FileFormats.Blueprints;
using FAForever.FileFormats.Lua;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: dotnet run tools/generate-unit-data.cs -- <fa checkout> [output file]");
    return 1;
}

string source = Path.GetFullPath(args[0]);
string output = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? "tools", "..", "src", "FAForever.Vault.Viewer", "wwwroot", "data", "units.json"));

// mod_info.lua is plain data Lua: version = 3839
IReadOnlyDictionary<string, LuaData> modInfo = LuaSourceParser.Execute(
    File.ReadAllText(Path.Combine(source, "mod_info.lua")), new Dictionary<string, LuaFunction>());
if (modInfo.GetValueOrDefault("version") is not LuaData.Number { Value: var version })
{
    Console.Error.WriteLine($"No version in {Path.Combine(source, "mod_info.lua")}");
    return 1;
}

List<BlueprintUnit> units = [];
foreach (string file in Directory.GetFiles(Path.Combine(source, "units"), "*_unit.bp", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
{
    string gamePath = "/" + Path.GetRelativePath(source, file).Replace(Path.DirectorySeparatorChar, '/');
    try
    {
        units.AddRange(BlueprintParser.Parse(File.ReadAllText(file), gamePath).OfType<BlueprintUnit>());
    }
    catch (FormatException exception)
    {
        Console.Error.WriteLine($"{gamePath}: {exception.Message}");
        return 1;
    }
}

UnitData data = UnitData.From((int)version, units);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
// LF line endings, no BOM: keep the generated file byte-stable across environments.
File.WriteAllText(output, data.Serialize(), new System.Text.UTF8Encoding(false));
Console.WriteLine($"Wrote {data.Units.Count} units of game version {data.GameVersion} to {output}");
return 0;
