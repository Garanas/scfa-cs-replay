using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using FAForever.FileFormats.Blueprints;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces.Serialized;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// The kinds of assets a map maker chooses from.
    /// </summary>
    public enum AssetKind
    {
        Prop,
        Unit,
        Decal,
        Texture,

        /// <summary>
        /// A brush's shape: a greyscale picture of the workspace's own (<see cref="WorkspaceLayout.Brushes"/>).
        /// </summary>
        Brush,
    }

    /// <summary>
    /// A fact about an asset, shown with it: <c>Reclaim</c>, <c>40 mass, 0 energy</c>.
    /// </summary>
    public sealed record AssetFact(string Label, string Value);

    /// <summary>
    /// The mesh of a prop or unit with what it is drawn with: files of the workspace's assets.
    /// </summary>
    /// <param name="Mesh">The mesh (<c>.glb</c>).</param>
    /// <param name="Albedo">Its albedo texture (<c>.png</c>), if the blueprint names one the archives have.</param>
    /// <param name="Normals">Its normal map, likewise.</param>
    /// <param name="Specular">Its specular map (a unit's <c>_specteam</c>), likewise.</param>
    /// <param name="Shader">The game's shader it is drawn with (<c>Unit</c>, <c>NormalMappedAlpha</c>).</param>
    public sealed record AssetModel(string Mesh, string? Albedo, string? Normals, string? Specular, string? Shader);

    /// <summary>
    /// An asset of the workspace as the map maker chooses it: a prop, a unit, a decal's texture, or
    /// a stratum's texture with its normal map.
    /// </summary>
    public sealed record AssetEntry
    {
        public required AssetKind Kind { get; init; }

        /// <summary>
        /// How a map names it, as <see cref="GameArchives.Normalize"/> writes a game path: a prop's
        /// blueprint (<c>env/evergreen/props/trees/pine06_prop.bp</c>), a decal's or a stratum's
        /// albedo texture (<c>env/tundra/layers/tund_rock_albedo.dds</c>), a unit's id (<c>uec1101</c>);
        /// a brush by its path in the workspace (<c>brushes/craters/crater01.png</c>).
        /// </summary>
        public required string Key { get; init; }

        /// <summary>
        /// Every name a map can use it by: <see cref="Key"/>, and a texture's normal map.
        /// </summary>
        public required ImmutableArray<string> Keys { get; init; }

        /// <summary>
        /// The file's name without its kind (<c>pine06_s2</c>, <c>tund_rock</c>), or a unit's
        /// description (<c>UEF Residential Building</c>).
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// A short code to search and sort by: the file's name, or a unit's id.
        /// </summary>
        public required string Code { get; init; }

        /// <summary>
        /// Where it belongs: the biome of the game's <c>env</c> folder (<c>evergreen</c>, <c>red barrens</c>),
        /// a unit's faction (<c>UEF</c>, <c>Other</c>), or a brush's folder in <c>brushes</c>
        /// (<c>craters</c>; <c>brushes</c> for one directly in it).
        /// </summary>
        public required string Group { get; init; }

        /// <summary>
        /// What it is within its kind: a prop's folder (<c>trees</c>, <c>rocks</c>, <c>groups</c>),
        /// a unit's layer (<c>structure</c>, <c>land</c>, <c>air</c>, <c>naval</c>), a decal's
        /// (<c>albedo</c>, <c>normals</c>, <c>splats</c>), a texture's (<c>stratum</c>, <c>macro</c>,
        /// <c>normal map only</c>).
        /// </summary>
        public required string Category { get; init; }

        /// <summary>
        /// The variants a prop shares a tile with: its name without the size and variant
        /// (<c>pine06_v1_s2</c> is of <c>pine06</c>); the name itself for the other kinds.
        /// </summary>
        public required string Family { get; init; }

        /// <summary>
        /// Whether a unit is civilian, the units maps place most.
        /// </summary>
        public bool IsCivilian { get; init; }

        /// <summary>
        /// The picture of it, a PNG of the workspace's assets: a decal's or texture's albedo, or
        /// its normal map when it has no albedo. Props and units are drawn from their <see cref="Model"/>.
        /// </summary>
        public string? Image { get; init; }

        /// <summary>
        /// A prop's or unit's most detailed mesh, which its picture is drawn from (<see cref="AssetPreview"/>).
        /// </summary>
        public AssetModel? Model { get; init; }

        /// <summary>
        /// A texture's normal map, a PNG of the workspace's assets.
        /// </summary>
        public string? NormalImage { get; init; }

        /// <summary>
        /// Its converted file in the workspace: the blueprint's JSON, or the texture's PNG.
        /// </summary>
        public required string File { get; init; }

        public ImmutableArray<AssetFact> Facts { get; init; } = [];
    }

    /// <summary>
    /// The assets of a workspace by kind, read from its converted <c>assets</c> folder
    /// (<see cref="WorkspaceLayout"/>): the props and units from their blueprints, the decals and
    /// stratum textures from the PNGs of the biomes' <c>decals</c>, <c>splats</c> and <c>layers</c>
    /// folders; and the brushes, the PNGs of its <c>brushes</c> folder, which are the map maker's
    /// own and not the game's.
    /// </summary>
    public sealed partial class AssetCatalog
    {
        private readonly Dictionary<string, AssetEntry> _byKey;

        private AssetCatalog(IReadOnlyList<AssetEntry> entries)
        {
            Entries = entries;
            _byKey = new Dictionary<string, AssetEntry>(StringComparer.Ordinal);
            foreach (AssetEntry entry in entries)
            {
                foreach (string key in entry.Keys)
                {
                    _byKey.TryAdd(key, entry);
                }
            }
        }

        public static AssetCatalog Empty { get; } = new AssetCatalog([]);

        /// <summary>
        /// Every asset, by kind, then group and name.
        /// </summary>
        public IReadOnlyList<AssetEntry> Entries { get; }

        /// <summary>
        /// The asset a map names by <paramref name="gamePath"/> (any of its <see cref="AssetEntry.Keys"/>,
        /// in any case, with or without a leading slash), or null.
        /// </summary>
        public AssetEntry? Find(string gamePath) =>
            _byKey.TryGetValue(KeyOf(gamePath), out AssetEntry? entry) ? entry : null;

        /// <summary>
        /// How a game path or unit id is written as a key: <see cref="GameArchives.Normalize"/>, and a
        /// <c>.png</c> of the assets is named by the game's <c>.dds</c>; a brush keeps its <c>.png</c>.
        /// </summary>
        public static string KeyOf(string gamePath)
        {
            string key = GameArchives.Normalize(gamePath);
            return key.EndsWith(".png", StringComparison.Ordinal) && !key.StartsWith(WorkspaceLayout.Brushes + "/", StringComparison.Ordinal)
                ? key[..^4] + ".dds"
                : key;
        }

        /// <summary>
        /// Reads the assets of a workspace: its converted <c>assets</c> folder and its brushes.
        /// </summary>
        public static AssetCatalog Load(Workspace workspace) => Load(workspace.AssetsFolder, workspace.BrushesFolder);

        /// <summary>
        /// Reads the assets of a workspace's <c>assets</c> folder, and the brushes of
        /// <paramref name="brushesFolder"/>; a folder that is not there has none. A blueprint that
        /// cannot be read is left out.
        /// </summary>
        public static AssetCatalog Load(string assetsFolder, string? brushesFolder = null)
        {
            List<AssetEntry> entries = [];
            if (brushesFolder is not null && Directory.Exists(brushesFolder))
            {
                entries.AddRange(Brushes(brushesFolder));
            }
            if (!Directory.Exists(assetsFolder))
            {
                return entries.Count == 0 ? Empty : new AssetCatalog(entries);
            }
            string env = Path.Combine(assetsFolder, "env");
            if (Directory.Exists(env))
            {
                foreach (string biomeFolder in Directory.EnumerateDirectories(env))
                {
                    string biome = Path.GetFileName(biomeFolder);
                    entries.AddRange(Props(assetsFolder, biomeFolder, biome));
                    entries.AddRange(Decals(assetsFolder, biomeFolder, biome));
                    entries.AddRange(Textures(assetsFolder, biomeFolder, biome));
                }
            }
            string units = Path.Combine(assetsFolder, "units");
            if (Directory.Exists(units))
            {
                entries.AddRange(Units(assetsFolder, units));
            }
            entries.Sort((a, b) => (a.Kind, a.Group, a.Name).CompareTo((b.Kind, b.Group, b.Name)));
            return new AssetCatalog(entries);
        }

        /// <summary>
        /// A prop's family: its name without a size (<c>_s2</c>) and variant (<c>_v1</c>).
        /// </summary>
        public static string FamilyOf(string propName) => VariantSuffix().Replace(propName, "");

        private static IEnumerable<AssetEntry> Props(string assets, string biomeFolder, string biome)
        {
            string props = Path.Combine(biomeFolder, "props");
            if (!Directory.Exists(props))
            {
                yield break;
            }
            foreach (string file in Directory.EnumerateFiles(props, "*_prop.json", SearchOption.AllDirectories))
            {
                PropBlueprintDto? prop = TryRead<PropBlueprintDto>(file);
                if (prop is null)
                {
                    continue;
                }
                string name = Path.GetFileName(file)[..^"_prop.json".Length];
                string folder = Path.GetFileName(Path.GetDirectoryName(file)!);
                List<AssetFact> facts = [];
                if (prop.Size is { } size)
                {
                    facts.Add(new AssetFact("Size", $"{Number(size.X)} × {Number(size.Z)} ogrids, {Number(size.Y)} high"));
                }
                facts.Add(new AssetFact("Reclaim", $"{Number(prop.ReclaimMass ?? 0)} mass, {Number(prop.ReclaimEnergy ?? 0)} energy"));
                if (prop.Health is double health)
                {
                    facts.Add(new AssetFact("Health", Number(health)));
                }
                string key = KeyOf(Relative(assets, file)[..^".json".Length] + ".bp");
                yield return new AssetEntry
                {
                    Kind = AssetKind.Prop,
                    Key = key,
                    Keys = [key],
                    Name = name,
                    Code = name,
                    Group = biome,
                    Category = string.Equals(folder, "props", StringComparison.OrdinalIgnoreCase) ? "other" : folder.ToLowerInvariant(),
                    Family = FamilyOf(name),
                    Model = ModelOf(assets, prop.Lods),
                    File = file,
                    Facts = [.. facts],
                };
            }
        }

        private static IEnumerable<AssetEntry> Decals(string assets, string biomeFolder, string biome)
        {
            foreach (string kind in new[] { "decals", "splats" })
            {
                string folder = Path.Combine(biomeFolder, kind);
                if (!Directory.Exists(folder))
                {
                    continue;
                }
                foreach (string file in Directory.EnumerateFiles(folder, "*.png"))
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    bool normals = NormalSuffix().IsMatch(stem);
                    string name = AlbedoSuffix().Replace(stem, "");
                    string key = KeyOf(Relative(assets, file));
                    yield return new AssetEntry
                    {
                        Kind = AssetKind.Decal,
                        Key = key,
                        Keys = [key],
                        Name = name,
                        Code = stem,
                        Group = biome,
                        Category = kind == "splats" ? "splats" : normals ? "normals" : "albedo",
                        Family = name,
                        Image = file,
                        File = file,
                    };
                }
            }
        }

        private static IEnumerable<AssetEntry> Textures(string assets, string biomeFolder, string biome)
        {
            string folder = Path.Combine(biomeFolder, "layers");
            if (!Directory.Exists(folder))
            {
                yield break;
            }
            Dictionary<string, (string? Albedo, string? Normals)> byName = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
            foreach (string file in Directory.EnumerateFiles(folder, "*.png"))
            {
                string stem = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                bool normals = NormalSuffix().IsMatch(stem);
                string name = normals ? NormalSuffix().Replace(stem, "") : AlbedoSuffix().Replace(stem, "");
                byName.TryGetValue(name, out (string? Albedo, string? Normals) pair);
                byName[name] = normals ? (pair.Albedo, pair.Normals ?? file) : (pair.Albedo ?? file, pair.Normals);
            }
            foreach ((string name, (string? albedo, string? normals)) in byName)
            {
                string main = albedo ?? normals!;
                string key = KeyOf(Relative(assets, main));
                List<AssetFact> facts = [new AssetFact("Normal map", normals is null ? "none" : Path.GetFileNameWithoutExtension(normals))];
                yield return new AssetEntry
                {
                    Kind = AssetKind.Texture,
                    Key = key,
                    Keys = normals is null || albedo is null ? [key] : [key, KeyOf(Relative(assets, normals))],
                    Name = name,
                    Code = name,
                    Group = biome,
                    Category = albedo is null ? "normal map only" : name.Contains("macro", StringComparison.Ordinal) ? "macro" : "stratum",
                    Family = name,
                    Image = main,
                    NormalImage = normals,
                    File = main,
                    Facts = [.. facts],
                };
            }
        }

        private static IEnumerable<AssetEntry> Units(string assets, string units)
        {
            foreach (string file in Directory.EnumerateFiles(units, "*_unit.json", SearchOption.AllDirectories))
            {
                UnitBlueprintDto? unit = TryRead<UnitBlueprintDto>(file);
                if (unit is null)
                {
                    continue;
                }
                string id = Path.GetFileName(file)[..^"_unit.json".Length].ToLowerInvariant();
                HashSet<string> categories = new HashSet<string>(unit.Categories, StringComparer.OrdinalIgnoreCase);
                string layer = categories.Contains("STRUCTURE") ? "structure"
                    : categories.Contains("NAVAL") ? "naval"
                    : categories.Contains("AIR") ? "air"
                    : categories.Contains("LAND") ? "land"
                    : "other";
                List<AssetFact> facts = [new AssetFact("Id", id)];
                if (unit.TechLevel is int tech)
                {
                    facts.Add(new AssetFact("Tech", tech == 4 ? "experimental" : $"T{tech}"));
                }
                if (unit.Footprint is { } footprint)
                {
                    facts.Add(new AssetFact("Footprint", $"{Number(footprint.SizeX)} × {Number(footprint.SizeZ)} ogrids"));
                }
                if (unit.MassCost is not null || unit.EnergyCost is not null)
                {
                    facts.Add(new AssetFact("Cost", $"{Number(unit.MassCost ?? 0)} mass, {Number(unit.EnergyCost ?? 0)} energy"));
                }
                if (unit.Health is double health)
                {
                    facts.Add(new AssetFact("Health", Number(health)));
                }
                yield return new AssetEntry
                {
                    Kind = AssetKind.Unit,
                    Key = id,
                    Keys = [id],
                    Name = unit.Name is { Length: > 0 } name ? name : id,
                    Code = id,
                    Group = unit.Faction is Faction faction and not Faction.Random ? faction.DisplayName() : "Other",
                    Category = layer,
                    Family = id,
                    IsCivilian = categories.Contains("CIVILIAN"),
                    Model = ModelOf(assets, unit.Lods),
                    File = file,
                    Facts = [.. facts],
                };
            }
        }

        private static IEnumerable<AssetEntry> Brushes(string folder)
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*.png", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(file);
                int slash = relative.IndexOf('/');
                List<AssetFact> facts = [];
                if (PngHeader(file) is (int width, int height, int depth, bool grey))
                {
                    facts.Add(new AssetFact("Size", $"{width} × {height} pixels"));
                    facts.Add(new AssetFact("Values", $"{depth}-bit {(grey ? "grey" : "colour")}"));
                }
                string key = WorkspaceLayout.Brushes + "/" + relative.ToLowerInvariant();
                yield return new AssetEntry
                {
                    Kind = AssetKind.Brush,
                    Key = key,
                    Keys = [key],
                    Name = name,
                    Code = name,
                    Group = slash < 0 ? WorkspaceLayout.Brushes : relative[..slash].ToLowerInvariant(),
                    Category = "shape",
                    Family = name,
                    Image = file,
                    File = file,
                    Facts = [.. facts],
                };
            }
        }

        /// <summary>
        /// A PNG's width, height, bits per value and whether it is grey, from its header; null for
        /// a file that is no PNG.
        /// </summary>
        private static (int Width, int Height, int Depth, bool Grey)? PngHeader(string file)
        {
            try
            {
                Span<byte> header = stackalloc byte[26];
                using FileStream stream = File.OpenRead(file);
                if (stream.Read(header) < header.Length || header[12] != 'I' || header[13] != 'H' || header[14] != 'D' || header[15] != 'R')
                {
                    return null;
                }
                int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..]);
                int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..]);
                // colour types 0 and 4 are grey, without and with alpha
                return (width, height, header[24], header[25] is 0 or 4);
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <summary>
        /// The first level of detail, the most detailed, with its paths in the assets folder.
        /// </summary>
        private static AssetModel? ModelOf(string assets, IReadOnlyList<MeshLodDto> lods) =>
            lods.Count == 0 ? null : new AssetModel(
                AssetFile(assets, lods[0].Mesh)!,
                AssetFile(assets, lods[0].Albedo),
                AssetFile(assets, lods[0].Normals),
                AssetFile(assets, lods[0].Specular),
                lods[0].Shader);

        private static string? AssetFile(string assets, string? path) => path is null ? null : Path.Combine(assets, path.TrimStart('/'));

        private static T? TryRead<T>(string file) where T : class
        {
            try
            {
                return ProjectJson.Read<T>(file);
            }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or MapProjectException)
            {
                return null;
            }
        }

        private static string Relative(string assets, string file) => Path.GetRelativePath(assets, file).Replace('\\', '/');

        private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        [GeneratedRegex("(_v[0-9]+)?(_s[0-9]+)?$")]
        private static partial Regex VariantSuffix();

        [GeneratedRegex("_normals?$", RegexOptions.IgnoreCase)]
        private static partial Regex NormalSuffix();

        [GeneratedRegex("_albedo$", RegexOptions.IgnoreCase)]
        private static partial Regex AlbedoSuffix();
    }

    /// <summary>
    /// How often a map uses each asset, by the asset's key (<see cref="AssetCatalog.KeyOf"/>): its
    /// props, decals, strata and the units of its armies.
    /// </summary>
    public static class AssetUse
    {
        public static IReadOnlyDictionary<string, int> Count(MapDocument document)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(string path)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    string key = AssetCatalog.KeyOf(path);
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
            }
            foreach (Prop prop in document.Props)
            {
                Add(prop.Blueprint);
            }
            foreach (Decal decal in document.Decals)
            {
                foreach (string texture in decal.Textures)
                {
                    Add(texture);
                }
            }
            foreach (TerrainLayer layer in document.Terrain.Layers)
            {
                Add(layer.AlbedoPath);
                Add(layer.NormalPath);
            }
            foreach (Army army in document.Armies)
            {
                AddUnits(army.Units);
            }
            void AddUnits(UnitGroup group)
            {
                foreach (Unit unit in group.Units)
                {
                    Add(unit.Blueprint);
                }
                foreach (UnitGroup child in group.Groups)
                {
                    AddUnits(child);
                }
            }
            return counts;
        }

        /// <summary>
        /// The biomes (<see cref="AssetEntry.Group"/>) of the props, decals and strata a map uses,
        /// most used first.
        /// </summary>
        public static IReadOnlyList<string> Biomes(IReadOnlyDictionary<string, int> uses) =>
            [.. uses
                .Where(use => use.Key.StartsWith("env/", StringComparison.Ordinal) && use.Key.IndexOf('/', 4) > 4)
                .GroupBy(use => use.Key[4..use.Key.IndexOf('/', 4)])
                .OrderByDescending(biome => biome.Sum(use => use.Value))
                .ThenBy(biome => biome.Key, StringComparer.Ordinal)
                .Select(biome => biome.Key)];
    }
}
