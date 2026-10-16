using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// An asset a map uses that neither the workspace nor the map's own files have (user story F-8).
    /// </summary>
    /// <param name="Path">How the map names it (a game path or a unit's id), as <see cref="AssetCatalog.KeyOf"/> writes it.</param>
    /// <param name="Uses">How often the map uses it.</param>
    /// <param name="Check">The problem check that finds it too: <c>missing-texture</c> or <c>missing-model</c>.</param>
    public sealed record MissingAsset(string Path, AssetKind Kind, int Uses, string Check);

    /// <summary>
    /// The assets a map uses that the workspace lacks, the same ones the problem checks
    /// <c>missing-texture</c> and <c>missing-model</c> find (<see cref="Checks.MapChecks"/>).
    /// </summary>
    public static class MissingAssets
    {
        /// <summary>
        /// What <paramref name="document"/> uses and neither <paramref name="catalog"/> nor its own
        /// files (<c>/maps/...</c>) have, most used first.
        /// </summary>
        public static IReadOnlyList<MissingAsset> Find(MapDocument document, AssetCatalog catalog)
        {
            Dictionary<string, (AssetKind Kind, int Uses)> found = new Dictionary<string, (AssetKind, int)>(StringComparer.Ordinal);
            void Add(string path, AssetKind kind)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }
                string key = AssetCatalog.KeyOf(path);
                if (found.TryGetValue(key, out (AssetKind Kind, int Uses) seen))
                {
                    found[key] = (seen.Kind, seen.Uses + 1);
                }
                else if (!Has(document, catalog, key))
                {
                    found[key] = (kind, 1);
                }
            }
            foreach (Prop prop in document.Props)
            {
                Add(prop.Blueprint, AssetKind.Prop);
            }
            foreach (Decal decal in document.Decals)
            {
                foreach (string texture in decal.Textures)
                {
                    Add(texture, AssetKind.Decal);
                }
            }
            foreach (TerrainLayer layer in document.Terrain.Layers)
            {
                Add(layer.AlbedoPath, AssetKind.Texture);
                Add(layer.NormalPath, AssetKind.Texture);
            }
            foreach (PlacedUnit placed in document.AllUnits())
            {
                Add(placed.Unit.Blueprint, AssetKind.Unit);
            }
            if (ChecksCubemaps(catalog))
            {
                foreach (string cubemap in CubemapsOf(document))
                {
                    Add(cubemap, AssetKind.Cubemap);
                }
            }
            return [.. found
                .Select(item => new MissingAsset(item.Key, item.Value.Kind, item.Value.Uses,
                    item.Value.Kind is AssetKind.Prop or AssetKind.Unit ? "missing-model" : "missing-texture"))
                .OrderByDescending(missing => missing.Uses)
                .ThenBy(missing => missing.Path, StringComparer.Ordinal)];
        }

        /// <summary>
        /// The cube maps a map names: the sky's, those units reflect and the water's.
        /// </summary>
        public static IEnumerable<string> CubemapsOf(MapDocument document) =>
            [document.Sky.Cubemap, .. document.Sky.EnvironmentCubemaps.Select(cubemap => cubemap.Texture), document.Water.Cubemap];

        /// <summary>
        /// Whether the workspace converted the game's cube maps (<c>textures/environment</c>), so
        /// that a cube map it lacks is missing; a workspace converted before it did knows none.
        /// </summary>
        public static bool ChecksCubemaps(AssetCatalog? catalog) =>
            catalog is not null && catalog.Entries.Any(entry => entry.Kind == AssetKind.Cubemap);

        private static bool Has(MapDocument document, AssetCatalog catalog, string key)
        {
            if (key.StartsWith("maps/", StringComparison.Ordinal))
            {
                // a file of the map's own folder: /maps/<folder>/<file>
                int folderEnd = key.IndexOf('/', "maps/".Length);
                string inFolder = folderEnd < 0 ? "" : key[(folderEnd + 1)..];
                return document.Files.Keys.Any(file => string.Equals(file, inFolder, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(AssetCatalog.KeyOf(file), inFolder, StringComparison.OrdinalIgnoreCase));
            }
            return catalog.Find(key) is not null;
        }
    }
}
