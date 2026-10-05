#!/usr/bin/env dotnet
// Adds one FAF release to the unit data of the Viewer: src/FAForever.Vault.Viewer/wwwroot/data/units/,
// an index.json (UnitDataIndex) with an entry per game version and the data files (UnitData), one unit
// per line. The game version comes from the checkout's mod_info.lua, so point it at a release tag. A
// release whose units equal those of a version already present reuses that version's file; otherwise
// it gets <version>.json. It prints which units changed compared with the previous version.
//
//   dotnet run tools/generate-unit-data.cs -- <fa checkout> [output folder] [--version <n>] [--commit <sha>] [--released <yyyy-mm-dd>]
//
// --version is the release (its tag); without it, mod_info.lua says. They can differ: release 3805
// still says 3804 there. --commit and --released record where the data comes from: the commit of the
// release tag and its date.
// A release without a full checkout: git -C <fa> archive refs/tags/3839 units mod_info.lua | tar -x -C <dir>
// Many releases at once, from scratch (after a change to UnitSummary): tools/backfill-unit-data.ps1

#:project ../src/FAForever.FileFormats.Blueprints/FAForever.FileFormats.Blueprints.csproj

using FAForever.FileFormats.Blueprints;
using FAForever.FileFormats.Lua;

const string Usage = "Usage: dotnet run tools/generate-unit-data.cs -- <fa checkout> [output folder] [--version <n>] [--commit <sha>] [--released <yyyy-mm-dd>]";

// positional arguments, and the options with their values
List<string> positional = [];
Dictionary<string, string> options = [];
for (int position = 0; position < args.Length; position++)
{
    if (args[position].StartsWith("--", StringComparison.Ordinal) && position + 1 < args.Length)
    {
        options[args[position]] = args[++position];
    }
    else
    {
        positional.Add(args[position]);
    }
}

if (positional.Count is < 1 or > 2 || options.Keys.Except(["--version", "--commit", "--released"]).Any())
{
    Console.Error.WriteLine(Usage);
    return 1;
}

DateOnly? released = null;
if (options.TryGetValue("--released", out string? releasedText))
{
    if (!DateOnly.TryParseExact(releasedText, "yyyy-MM-dd", out DateOnly date))
    {
        Console.Error.WriteLine($"--released is not a date (yyyy-mm-dd): {releasedText}");
        return 1;
    }
    released = date;
}

string source = Path.GetFullPath(positional[0]);
string output = positional.Count > 1
    ? Path.GetFullPath(positional[1])
    : Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? "tools", "..", "src", "FAForever.Vault.Viewer", "wwwroot", "data", "units"));
string indexPath = Path.Combine(output, "index.json");

// mod_info.lua is plain data Lua: version = 3839
IReadOnlyDictionary<string, LuaData> modInfo = LuaSourceParser.Execute(
    File.ReadAllText(Path.Combine(source, "mod_info.lua")), new Dictionary<string, LuaFunction>());
int? modInfoVersion = modInfo.GetValueOrDefault("version") is LuaData.Number { Value: var number } ? (int)number : null;
int? requestedVersion = options.TryGetValue("--version", out string? versionText) && int.TryParse(versionText, out int parsed) ? parsed : null;
if (options.ContainsKey("--version") && requestedVersion is null)
{
    Console.Error.WriteLine($"--version is not a number: {versionText}");
    return 1;
}
if ((requestedVersion ?? modInfoVersion) is not { } version)
{
    Console.Error.WriteLine($"No version in {Path.Combine(source, "mod_info.lua")}; pass --version");
    return 1;
}
if (requestedVersion is not null && modInfoVersion is not null && requestedVersion != modInfoVersion)
{
    Console.WriteLine($"Note: mod_info.lua says {modInfoVersion}; using {version}.");
}

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

UnitData data = UnitData.From(blueprints);
Directory.CreateDirectory(output);
UnitDataIndex index = File.Exists(indexPath) ? UnitDataIndex.Deserialize(File.ReadAllText(indexPath)) : UnitDataIndex.Empty;
UnitData Read(int owner) => UnitData.Deserialize(File.ReadAllText(Path.Combine(output, index.Versions[owner].File)));

// What changed since the previous version (the newest older one), for the index and the pull request.
UnitDataIndex.UnitChanges? changes = null;
if (index.Previous(version) is { } previous)
{
    changes = UnitDataIndex.UnitChanges.From(previous, UnitData.Compare(Read(previous), data));
    string Names(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => $"{DisplayName(data.GetOrNull(id))} ({id})"));
    Console.WriteLine(changes.IsEmpty
        ? $"No unit changed since {previous}."
        : $"Since {previous}: {changes.Changed.Count} changed, {changes.Added.Count} added, {changes.Removed.Count} removed.");
    if (changes.Changed.Count > 0) Console.WriteLine($"Changed: {Names(changes.Changed)}");
    if (changes.Added.Count > 0) Console.WriteLine($"Added: {Names(changes.Added)}");
    if (changes.Removed.Count > 0) Console.WriteLine($"Removed: {string.Join(", ", changes.Removed)}");
}

// Reuse the file of a version with exactly these units (one that owns its file, the oldest first);
// otherwise write this version's own file.
int? reusedFrom = index.Versions
    .Where(candidate => candidate.Key != version && candidate.Value.ReusedFrom is null && File.Exists(Path.Combine(output, candidate.Value.File)))
    .OrderBy(candidate => candidate.Key)
    .Where(candidate => Read(candidate.Key).HasSameUnits(data))
    .Select(candidate => (int?)candidate.Key)
    .FirstOrDefault();
string dataFile = reusedFrom is { } owner ? index.Versions[owner].File : $"{version}.json";
if (reusedFrom is null)
{
    // Other versions may reuse this version's file: they would change with it.
    if (index.Versions.Any(candidate => candidate.Key != version && candidate.Value.File == dataFile))
    {
        Console.Error.WriteLine($"{dataFile} is reused by other versions; regenerate them all with tools/backfill-unit-data.ps1.");
        return 1;
    }

    // LF line endings, no BOM: keep the generated files byte-stable across environments.
    File.WriteAllText(Path.Combine(output, dataFile), data.Serialize(), new System.Text.UTF8Encoding(false));
}

UnitDataIndex.Entry entry = new(
    dataFile,
    data.Units.Count,
    reusedFrom,
    changes,
    options.GetValueOrDefault("--commit"),
    released,
    DateOnly.FromDateTime(DateTime.UtcNow));
File.WriteAllText(indexPath, index.With(version, entry).Serialize(), new System.Text.UTF8Encoding(false));
Console.WriteLine($"Game version {version}: {data.Units.Count} units in {dataFile}{(reusedFrom is not null ? $" (the units of {reusedFrom})" : "")}");
return 0;

static string DisplayName(UnitSummary? unit) => unit?.Name ?? unit?.Description ?? "?";
