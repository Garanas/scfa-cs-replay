using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// How one asset a map names was found: the file the editor reads for it, or why there is none.
    /// </summary>
    /// <param name="Kind">texture, decal, cubemap, water, prop or unit.</param>
    /// <param name="Path">As the map names it (the first way it is written, when several differ in case).</param>
    /// <param name="Uses">How often the map names it.</param>
    /// <param name="File">The file read for it (a converted asset, or the map's own file as <c>map:&lt;path&gt;</c>); null when unresolved.</param>
    /// <param name="Reason">Why it did not resolve; null when it did.</param>
    public sealed record AssetLookup(string Kind, string Path, int Uses, string? File, string? Reason)
    {
        public bool Resolved => File is not null;
    }

    /// <summary>
    /// Resolves every texture, decal, cube map, prop and unit a map names the way the editor shows
    /// them (<see cref="GameTextures"/> for pictures, <see cref="AssetCatalog"/> and the model's files
    /// for props and units), down to the files on disk. Proves that the lookups work whatever the case
    /// a map writes its paths in, on a system whose file system minds case (<c>faf-map dev resolve</c>).
    /// </summary>
    public static class AssetResolution
    {
        /// <summary>
        /// Every asset <paramref name="document"/> names, by kind and path, each once.
        /// </summary>
        public static IReadOnlyList<AssetLookup> Resolve(MapDocument document, Workspace? workspace, AssetCatalog catalog)
        {
            Dictionary<(string Kind, string Key), (string Path, int Uses)> named = [];
            void Name(string kind, string? path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }
                (string, string) id = (kind, AssetCatalog.KeyOf(path));
                named[id] = named.TryGetValue(id, out (string Path, int Uses) seen) ? (seen.Path, seen.Uses + 1) : (path, 1);
            }

            foreach (TerrainLayer layer in document.Terrain.Layers)
            {
                Name("texture", layer.AlbedoPath);
                Name("texture", layer.NormalPath);
            }
            foreach (Decal decal in document.Decals)
            {
                foreach (string texture in decal.Textures)
                {
                    Name("decal", texture);
                }
            }
            foreach (string cubemap in MissingAssets.CubemapsOf(document))
            {
                Name("cubemap", cubemap);
            }
            Name("water", document.Water.Ramp);
            foreach (WaveTexture wave in document.Water.WaveTextures)
            {
                Name("water", wave.Texture);
            }
            foreach (Prop prop in document.Props)
            {
                Name("prop", prop.Blueprint);
            }
            foreach (PlacedUnit placed in document.AllUnits())
            {
                Name("unit", placed.Unit.Blueprint);
            }

            return [.. named
                .Select(item => Lookup(item.Key.Kind, item.Value.Path, item.Value.Uses, document, workspace, catalog))
                .OrderBy(lookup => lookup.Kind, StringComparer.Ordinal)
                .ThenBy(lookup => lookup.Path, StringComparer.OrdinalIgnoreCase)];
        }

        private static AssetLookup Lookup(string kind, string path, int uses, MapDocument document, Workspace? workspace, AssetCatalog catalog)
        {
            if (kind is "prop" or "unit")
            {
                return Model(kind, path, uses, document, catalog);
            }
            TextureSource? source = kind == "cubemap"
                ? GameTextures.FindCube(path, document, workspace)
                : GameTextures.Find(path, document, workspace);
            if (source is null)
            {
                return new AssetLookup(kind, path, uses, null, IsMapPath(path) ? "not among the map's files" : "not among the converted assets");
            }
            string key = source.Key.StartsWith("cube:", StringComparison.Ordinal) ? source.Key["cube:".Length..] : source.Key;
            string? file = key.StartsWith("map:", StringComparison.Ordinal) ? key : workspace?.FindAsset(key);
            return new AssetLookup(kind, path, uses, file, file is null ? "found, but its file is gone" : null);
        }

        private static AssetLookup Model(string kind, string path, int uses, MapDocument document, AssetCatalog catalog)
        {
            if (IsMapPath(path))
            {
                // a map's own prop: the editor shows it as a box (the catalogue holds the game's)
                string own = MapOwnPath(path);
                string? file = document.Files.Keys.FirstOrDefault(name => string.Equals(name, own, StringComparison.OrdinalIgnoreCase));
                return new AssetLookup(kind, path, uses, file is null ? null : "map:" + file, file is null ? "not among the map's files" : null);
            }
            if (catalog.Find(path) is not AssetEntry entry)
            {
                return new AssetLookup(kind, path, uses, null, "not in the catalogue");
            }
            if (entry.Model is not AssetModel model)
            {
                return new AssetLookup(kind, path, uses, null, "the catalogue knows no model for it");
            }
            if (!System.IO.File.Exists(model.Mesh))
            {
                return new AssetLookup(kind, path, uses, null, $"its mesh {model.Mesh} is gone");
            }
            if (model.Albedo is string albedo && !System.IO.File.Exists(albedo))
            {
                return new AssetLookup(kind, path, uses, null, $"its texture {albedo} is gone");
            }
            return new AssetLookup(kind, path, uses, model.Mesh, null);
        }

        private static bool IsMapPath(string path) => GameArchives.Normalize(path).StartsWith("maps/", StringComparison.Ordinal);

        private static string MapOwnPath(string path)
        {
            string key = GameArchives.Normalize(path);
            int folderEnd = key.IndexOf('/', "maps/".Length);
            return folderEnd < 0 ? "" : path.Replace('\\', '/').TrimStart('/')[(folderEnd + 1)..];
        }
    }
}
