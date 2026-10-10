using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// What a texture is in a decal set, by the suffix of its name.
    /// </summary>
    public enum DecalSetPart
    {
        /// <summary>No suffix of a set: a decal of its own.</summary>
        Plain,

        /// <summary><c>_albedo</c>: the colour, an albedo decal.</summary>
        Albedo,

        /// <summary><c>_normals</c> (or <c>_normal</c>): a normals decal with the same place, turn and size.</summary>
        Normals,

        /// <summary><c>_spec</c>: the albedo decal's second texture.</summary>
        Specular,

        /// <summary><c>_mask</c> or <c>_mask_&lt;name&gt;</c>: a greyscale mask, never exported.</summary>
        Mask,
    }

    /// <summary>
    /// A texture's place in a decal set: the set's name (its path without suffix and extension), its
    /// part, and for a mask its name (<c>main</c> for the plain <c>_mask</c>).
    /// </summary>
    public sealed record DecalSetFile(string Name, DecalSetPart Part, string? MaskName = null);

    /// <summary>
    /// A decal set of the workspace's assets: its name and the keys of its textures (user story D-1).
    /// </summary>
    /// <param name="Name">The game path without suffix and extension, as a key (<c>env/common/decals/rock01</c>).</param>
    public sealed record DecalSetAssets(string Name, string? Albedo, string? Normals, string? Specular, ImmutableSortedDictionary<string, string> Masks)
    {
        /// <summary>
        /// The texture whose picture stands for the set: its albedo, else its normals, else a mask.
        /// </summary>
        public string? First => Albedo ?? Normals ?? Masks.Values.FirstOrDefault();

        /// <summary>
        /// How many textures it has.
        /// </summary>
        public int Count => (Albedo is null ? 0 : 1) + (Normals is null ? 0 : 1) + (Specular is null ? 0 : 1) + Masks.Count;

        /// <summary>
        /// Its parts in words: <c>albedo, normals, specular, masks main and moss</c>.
        /// </summary>
        public string Summary => string.Join(", ", new[]
        {
            Albedo is null ? null : "albedo",
            Normals is null ? null : "normals",
            Specular is null ? null : "specular",
            Masks.Count == 0 ? null : Masks.Count == 1 ? $"mask {Masks.Keys.First()}" : $"masks {string.Join(", ", Masks.Keys.SkipLast(1))} and {Masks.Keys.Last()}",
        }.OfType<string>());

        public bool Equals(DecalSetAssets? other) =>
            other is not null && Name == other.Name && Albedo == other.Albedo && Normals == other.Normals && Specular == other.Specular
            && Masks.Count == other.Masks.Count && Masks.All(mask => other.Masks.TryGetValue(mask.Key, out string? key) && key == mask.Value);

        public override int GetHashCode() => HashCode.Combine(Name, Albedo, Normals, Specular, Masks.Count);
    }

    /// <summary>
    /// Decal sets (user story D-1): several textures with one name and a suffix each
    /// (<c>rock01_albedo</c>, <c>rock01_normals</c>, <c>rock01_spec</c>, <c>rock01_mask</c>,
    /// <c>rock01_mask_moss</c>), placed and moved as one decal (<see cref="Decal.Set"/>) and written
    /// as one decal per texture: the albedo with its specular as second texture, then the normals
    /// with the same place, turn and size. Masks are never exported; decal-masks layers read them.
    /// </summary>
    public static partial class DecalSets
    {
        /// <summary>
        /// The name of a set's plain <c>_mask</c>.
        /// </summary>
        public const string MainMask = "main";

        /// <summary>
        /// A texture's place in a set, from its path or file name (the extension is left out).
        /// </summary>
        public static DecalSetFile PartOf(string path)
        {
            string normalized = path.Replace('\\', '/');
            int dot = normalized.LastIndexOf('.');
            if (dot > normalized.LastIndexOf('/'))
            {
                normalized = normalized[..dot];
            }
            Match match = Suffix().Match(normalized);
            if (!match.Success)
            {
                return new DecalSetFile(normalized, DecalSetPart.Plain);
            }
            string suffix = match.Groups["suffix"].Value.ToLowerInvariant();
            string name = match.Groups["name"].Value;
            return suffix switch
            {
                "albedo" => new DecalSetFile(name, DecalSetPart.Albedo),
                "normal" or "normals" => new DecalSetFile(name, DecalSetPart.Normals),
                "spec" or "specular" => new DecalSetFile(name, DecalSetPart.Specular),
                _ => new DecalSetFile(name, DecalSetPart.Mask, match.Groups["mask"].Success ? match.Groups["mask"].Value.ToLowerInvariant() : MainMask),
            };
        }

        /// <summary>
        /// The decal sets among a catalogue's decals: textures of one folder and name with at least
        /// two parts, or a mask. The keys are the catalogue's (<see cref="AssetEntry.Key"/>).
        /// </summary>
        public static IReadOnlyList<DecalSetAssets> Of(IEnumerable<AssetEntry> entries)
        {
            List<DecalSetAssets> sets = [];
            foreach (IGrouping<string, (AssetEntry Entry, DecalSetFile File)> group in entries
                .Where(entry => entry.Kind == AssetKind.Decal)
                .Select(entry => (Entry: entry, File: PartOf(entry.Key)))
                .Where(item => item.File.Part != DecalSetPart.Plain)
                .GroupBy(item => item.File.Name, StringComparer.Ordinal))
            {
                string? Of(DecalSetPart part) => group.FirstOrDefault(item => item.File.Part == part).Entry?.Key;
                ImmutableSortedDictionary<string, string> masks = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
                    group.Where(item => item.File.Part == DecalSetPart.Mask)
                        .GroupBy(item => item.File.MaskName!, StringComparer.Ordinal)
                        .Select(mask => KeyValuePair.Create(mask.Key, mask.First().Entry.Key)));
                DecalSetAssets set = new DecalSetAssets(group.Key, Of(DecalSetPart.Albedo), Of(DecalSetPart.Normals), Of(DecalSetPart.Specular), masks);
                if (set.Count >= 2 || masks.Count > 0)
                {
                    sets.Add(set);
                }
            }
            return sets;
        }

        /// <summary>
        /// The set a key names among a catalogue's decals: by its name (<c>env/common/decals/rock01</c>,
        /// with or without a leading slash), or null.
        /// </summary>
        public static DecalSetAssets? Find(IEnumerable<AssetEntry> entries, string name)
        {
            string key = GameArchives.Normalize(name);
            return Of(entries).FirstOrDefault(set => string.Equals(set.Name, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The decal sets among the map's own files (<see cref="MapDocument.Files"/>: a map maker's
        /// own textures and masks, which the export writes into the map's folder), with game paths
        /// into the map's folder (<c>/maps/&lt;folder&gt;/env/decals/rock01_albedo.png</c>) as keys.
        /// </summary>
        public static IReadOnlyList<DecalSetAssets> OfMap(MapDocument document)
        {
            List<DecalSetAssets> sets = [];
            string root = $"/maps/{document.Folder}/";
            foreach (IGrouping<string, (string Path, DecalSetFile File)> group in document.Files.Keys
                .Where(path => path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .Select(path => (Path: root + path, File: PartOf(root + path)))
                .Where(item => item.File.Part != DecalSetPart.Plain)
                .GroupBy(item => item.File.Name, StringComparer.OrdinalIgnoreCase))
            {
                string? Of(DecalSetPart part) => group.FirstOrDefault(item => item.File.Part == part).Path;
                ImmutableSortedDictionary<string, string> masks = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
                    group.Where(item => item.File.Part == DecalSetPart.Mask)
                        .GroupBy(item => item.File.MaskName!, StringComparer.Ordinal)
                        .Select(mask => KeyValuePair.Create(mask.Key, mask.First().Path)));
                DecalSetAssets set = new DecalSetAssets(group.Key, Of(DecalSetPart.Albedo), Of(DecalSetPart.Normals), Of(DecalSetPart.Specular), masks);
                if ((set.Count >= 2 || masks.Count > 0) && (set.Albedo is not null || set.Normals is not null))
                {
                    sets.Add(set);
                }
            }
            return sets;
        }

        /// <summary>
        /// The set a name names: among a catalogue's decals, else among the map's own files.
        /// </summary>
        public static DecalSetAssets? Find(IEnumerable<AssetEntry> entries, MapDocument? document, string name) =>
            Find(entries, name)
            ?? (document is null ? null : OfMap(document).FirstOrDefault(set => string.Equals(set.Name.TrimStart('/'), name.Replace('\\', '/').TrimStart('/'), StringComparison.OrdinalIgnoreCase)));

        /// <summary>
        /// The set a decal of the map belongs to as the map names it (<see cref="Decal.Set"/>), from
        /// the assets of a set: the game paths of its textures.
        /// </summary>
        public static DecalSet ToDecalSet(DecalSetAssets set) => new DecalSet
        {
            Name = DecalEdits.TexturePath(set.Name),
            Normals = set.Albedo is null ? null : set.Normals is string normals ? DecalEdits.TexturePath(normals) : null,
            Masks = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, set.Masks.Select(mask => KeyValuePair.Create(mask.Key, DecalEdits.TexturePath(mask.Value)))),
        };

        /// <summary>
        /// The map's decals as the game gets them: each decal set as one decal per texture (its own,
        /// then its normals with the same place, turn, size and distances, under the next free id, in
        /// the same groups), and no decal carrying a set. The document itself when it has no sets.
        /// </summary>
        public static MapDocument Expand(MapDocument document)
        {
            if (!document.Decals.Any(decal => decal.Set is not null))
            {
                return document;
            }
            int next = document.Decals.Max(decal => decal.Id) + 1;
            List<Decal> decals = new List<Decal>(document.Decals.Length);
            Dictionary<int, int> normalsOf = [];
            foreach (Decal decal in document.Decals)
            {
                decals.Add(decal with { Set = null });
                if (decal.Set?.Normals is string normals && decal.Type != DecalType.Normals && normals.Length > 0)
                {
                    normalsOf[decal.Id] = next;
                    decals.Add(decal with { Id = next++, Type = DecalType.Normals, Textures = [normals, ""], Set = null });
                }
            }
            return document with
            {
                Decals = [.. decals],
                DecalGroups = [.. document.DecalGroups.Select(group => group.DecalIds.Any(normalsOf.ContainsKey)
                    ? group with { DecalIds = [.. group.DecalIds.SelectMany(id => normalsOf.TryGetValue(id, out int added) ? new[] { id, added } : [id])] }
                    : group)],
            };
        }

        /// <summary>
        /// The masks of a name the map's decal sets have, by decal: the decal and the game path of
        /// its mask. <paramref name="sets"/> limits it to sets of those names (empty for all).
        /// </summary>
        public static IEnumerable<(Decal Decal, string Mask)> MasksOf(MapDocument document, string maskName, IReadOnlyCollection<string> sets)
        {
            foreach (Decal decal in document.Decals)
            {
                if (decal.Set is DecalSet set && set.Masks.TryGetValue(maskName, out string? mask)
                    && (sets.Count == 0 || sets.Any(name => string.Equals(GameArchives.Normalize(name), GameArchives.Normalize(set.Name), StringComparison.OrdinalIgnoreCase))))
                {
                    yield return (decal, mask);
                }
            }
        }

        [GeneratedRegex("^(?<name>.+?)_(?<suffix>albedo|normals?|spec|specular|mask(?:_(?<mask>[a-z0-9]+))?)$", RegexOptions.IgnoreCase)]
        private static partial Regex Suffix();
    }
}
