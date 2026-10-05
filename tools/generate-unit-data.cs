#!/usr/bin/env dotnet
// Adds one FAF release to the unit data of the Viewer: src/FAForever.Vault.Viewer/wwwroot/data/units/,
// an index.json (UnitDataIndex) that maps every game version to a data file (UnitData), one unit per
// line. The game version comes from the checkout's mod_info.lua, so point it at a release tag. A
// release whose units equal those of a version already present shares that version's file; otherwise
// it gets <version>.json. It prints which units changed compared with the previous version.
//
//   dotnet run tools/generate-unit-data.cs -- <fa checkout> [output folder]
//
// A release without a full checkout: git -C <fa> archive refs/tags/3839 units mod_info.lua | tar -x -C <dir>
// Many releases at once, from scratch (after a change to UnitSummary): tools/backfill-unit-data.ps1

#:project ../src/FAForever.FileFormats.Blueprints/FAForever.FileFormats.Blueprints.csproj

using FAForever.FileFormats.Blueprints;
using FAForever.FileFormats.Lua;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: dotnet run tools/generate-unit-data.cs -- <fa checkout> [output folder]");
    return 1;
}

string source = Path.GetFullPath(args[0]);
string output = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? "tools", "..", "src", "FAForever.Vault.Viewer", "wwwroot", "data", "units"));
string indexPath = Path.Combine(output, "index.json");

// mod_info.lua is plain data Lua: version = 3839
IReadOnlyDictionary<string, LuaData> modInfo = LuaSourceParser.Execute(
    File.ReadAllText(Path.Combine(source, "mod_info.lua")), new Dictionary<string, LuaFunction>());
if (modInfo.GetValueOrDefault("version") is not LuaData.Number { Value: var versionNumber })
{
    Console.Error.WriteLine($"No version in {Path.Combine(source, "mod_info.lua")}");
    return 1;
}
int version = (int)versionNumber;

List<BlueprintUnit> blueprints = [];
foreach (string file in Directory.GetFiles(Path.Combine(source, "units"), "*_unit.bp", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
{
    string gamePath = "/" + Path.GetRelativePath(source, file).Replace(Path.DirectorySeparatorChar, '/');
    try
    {
        blueprints.AddRange(BlueprintParser.Parse(File.ReadAllText(file), gamePath).OfType<BlueprintUnit>());
    }
    catch (FormatException exception)
    {
        Console.Error.WriteLine($"{gamePath}: {exception.Message}");
        return 1;
    }
}

UnitData data = UnitData.From(version, blueprints);
Directory.CreateDirectory(output);
UnitDataIndex index = File.Exists(indexPath) ? UnitDataIndex.Deserialize(File.ReadAllText(indexPath)) : UnitDataIndex.Empty;
UnitData Read(string file) => UnitData.Deserialize(File.ReadAllText(Path.Combine(output, file)));

// What changed since the previous version (the newest older one), for the pull request.
if (index.Versions.Keys.Where(candidate => candidate < version).DefaultIfEmpty().Max() is var previous and > 0)
{
    UnitData.Difference difference = UnitData.Compare(Read(index.Versions[previous]), data);
    string Names(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => $"{UnitFilterName(data.GetOrNull(id))} ({id})"));
    Console.WriteLine(difference.IsEmpty
        ? $"No unit changed since {previous}."
        : $"Since {previous}: {difference.Changed.Count} changed, {difference.Added.Count} added, {difference.Removed.Count} removed.");
    if (difference.Changed.Count > 0) Console.WriteLine($"Changed: {Names(difference.Changed)}");
    if (difference.Added.Count > 0) Console.WriteLine($"Added: {Names(difference.Added)}");
    if (difference.Removed.Count > 0) Console.WriteLine($"Removed: {string.Join(", ", difference.Removed)}");
}

// Share the file of a version with the same units; otherwise write this version's own file.
string? shared = index.Versions
    .Where(entry => entry.Key != version)
    .Select(entry => entry.Value)
    .Distinct()
    .FirstOrDefault(file => File.Exists(Path.Combine(output, file)) && Read(file).HasSameUnits(data));
string dataFile = shared ?? $"{version}.json";
if (shared is null)
{
    // Another version may point at this version's file: it would change with it.
    if (index.Versions.Any(entry => entry.Key != version && entry.Value == dataFile))
    {
        Console.Error.WriteLine($"{dataFile} is shared with other versions; regenerate them all with tools/backfill-unit-data.ps1.");
        return 1;
    }

    // LF line endings, no BOM: keep the generated files byte-stable across environments.
    File.WriteAllText(Path.Combine(output, dataFile), data.Serialize(), new System.Text.UTF8Encoding(false));
}

File.WriteAllText(indexPath, index.With(version, dataFile).Serialize(), new System.Text.UTF8Encoding(false));
Console.WriteLine($"Game version {version}: {data.Units.Count} units in {dataFile}{(shared is not null ? " (shared, no unit changed)" : "")}");
return 0;

static string UnitFilterName(UnitSummary? unit) => unit?.Name ?? unit?.Description ?? "?";
