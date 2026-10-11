using System.Collections.Immutable;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// The map's own files as assets of the shelf and the picker (user story D-1): a map maker's own
    /// decals and decal sets, which lie in the map's folder (<see cref="MapDocument.Files"/>, the
    /// project's <c>files/</c>) and not among the workspace's converted assets.
    /// </summary>
    /// <remarks>
    /// A decal of the map is a picture in a folder named <c>decals</c> (<c>env/decals/rock01_albedo.png</c>),
    /// or a picture a decal of the map names. Its key is its game path without the leading slash, with
    /// the file's own name and extension (<c>maps/rocks.v0001/env/decals/rock01_albedo.png</c>): a decal
    /// placed from it names the file as it is, which the export converts to DDS with the reference.
    /// </remarks>
    public static class MapOwnAssets
    {
        /// <summary>
        /// The group (the shelf's biome column) of the map's own assets.
        /// </summary>
        public const string Group = "this map";

        private static readonly string[] PictureExtensions = [".dds", ".png", ".tga", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp"];

        /// <summary>
        /// Whether a file of the map is a picture, by its extension.
        /// </summary>
        public static bool IsPicture(string path) =>
            PictureExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The files of the map that are decals: pictures in a <c>decals</c> folder, and pictures a decal
        /// of the map names; by their path in the map's folder, in the order of the files.
        /// </summary>
        public static IReadOnlyList<string> DecalFiles(MapDocument document)
        {
            HashSet<string> named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Decal decal in document.Decals)
            {
                foreach (string texture in decal.Textures)
                {
                    if (InFolder(texture) is string own)
                    {
                        named.Add(own);
                    }
                }
            }
            return [.. document.Files.Keys.Where(path => IsPicture(path)
                && (named.Contains(path) || path.Split('/').SkipLast(1).Any(folder => string.Equals(folder, "decals", StringComparison.OrdinalIgnoreCase))))];
        }

        /// <summary>
        /// The key of a file of the map as an asset: its game path without the leading slash
        /// (<c>maps/rocks.v0001/env/decals/rock01_albedo.png</c>).
        /// </summary>
        public static string KeyOf(MapDocument document, string path) => $"maps/{document.Folder}/{path}";

        /// <summary>
        /// The decals among the map's own files as assets of the kind <see cref="AssetKind.Decal"/>, in
        /// the group <see cref="Group"/>, with their category from the decal set's part (<c>albedo</c>,
        /// <c>normals</c>, <c>specular</c>, <c>masks</c>), and a decal set's name as their family.
        /// </summary>
        /// <param name="picture">The PNG on disk that shows a file (its path in the map's folder and
        /// its bytes), for the tiles; null while there is none.</param>
        public static IReadOnlyList<AssetEntry> Decals(MapDocument document, Func<string, ImmutableArray<byte>, string?> picture)
        {
            List<AssetEntry> entries = [];
            foreach (string path in DecalFiles(document))
            {
                string key = KeyOf(document, path);
                DecalSetFile part = DecalSets.PartOf(key);
                string name = Path.GetFileNameWithoutExtension(path);
                string category = part.Part switch
                {
                    DecalSetPart.Normals => "normals",
                    DecalSetPart.Specular => "specular",
                    DecalSetPart.Mask => "masks",
                    _ => DecalEdits.TypeOf(path) == DecalType.Normals ? "normals" : "albedo",
                };
                string? image = picture(path, document.Files[path]);
                entries.Add(new AssetEntry
                {
                    Kind = AssetKind.Decal,
                    Key = key,
                    Keys = [.. new[] { key, AssetCatalog.KeyOf(key) }.Distinct(StringComparer.Ordinal)],
                    Name = name,
                    Code = Path.GetFileName(path),
                    Group = Group,
                    Category = category,
                    Family = part.Part == DecalSetPart.Plain ? name : Path.GetFileName(part.Name),
                    Image = image,
                    File = image ?? path,
                    Facts = [new AssetFact("File", path), new AssetFact("Size", $"{document.Files[path].Length / 1024.0:0.#} KB")],
                });
            }
            return entries;
        }

        /// <summary>
        /// The asset of <paramref name="key"/> among <paramref name="entries"/> of the map's own files:
        /// by its key or game path in any case, with or without a leading slash; null for none.
        /// </summary>
        public static AssetEntry? Find(IEnumerable<AssetEntry> entries, string key)
        {
            string normalized = key.Replace('\\', '/').TrimStart('/');
            return entries.FirstOrDefault(entry => string.Equals(entry.Key, normalized, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The path in the map's folder a game path into it names (<c>/maps/x.v0001/env/a.png</c> is
        /// <c>env/a.png</c>); null for any other path.
        /// </summary>
        public static string? InFolder(string gamePath)
        {
            string key = gamePath.Replace('\\', '/').TrimStart('/');
            if (!key.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            int folderEnd = key.IndexOf('/', "maps/".Length);
            return folderEnd < 0 ? null : key[(folderEnd + 1)..];
        }
    }
}
