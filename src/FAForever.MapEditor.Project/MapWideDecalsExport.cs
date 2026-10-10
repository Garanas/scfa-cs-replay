using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// What the export made of one map-wide decal: its kind, the texture it named and how it came
    /// about (generated, a file converted, a file copied, the map's own file).
    /// </summary>
    public sealed record MapWideWritten(MapWideKind Kind, string Texture, string How);

    /// <summary>
    /// Turns the map-wide decals (<see cref="MapDocument.WideDecals"/>) into decals over the whole
    /// map and their textures into the map's own files, for the version an export writes: generated
    /// ones made from the final heightmap, files copied (a DDS) or converted to DDS.
    /// </summary>
    /// <remarks>
    /// The layout is Kaali's (kaali.v0004, which has exactly these three decals): each at the map's
    /// corner (0, height at the corner, 0), as large as the map (its scale the map's width, width and
    /// height), not turned, with a cut-off of 10000 so it always shows; the albedo decal with its
    /// specular as the second texture, the shadow decal an albedo decal (black, the darkness in
    /// alpha), the normals decal of type normals; in that order, under every other decal. Textures
    /// are DXT5 with a full chain of mipmaps, the specular DXT1, in <c>env/decals/</c> of the map's
    /// folder (<c>map_albedo.dds</c>, <c>map_specular.dds</c>, <c>map_lighting.dds</c>,
    /// <c>map_normals.dds</c>); generated ones 16 pixels per ogrid for the normals and 4 for the
    /// shadow, at most 8192 on a side (<see cref="MapWideDecalImages"/>).
    /// </remarks>
    public static class MapWideDecalsExport
    {
        /// <summary>
        /// The distance beyond which a map-wide decal is not drawn: beyond any zoom.
        /// </summary>
        public const float CutOffLod = 10000;

        /// <summary>
        /// Where the textures go in the map's folder.
        /// </summary>
        public const string Folder = "env/decals";

        /// <summary>
        /// The document with its map-wide decals as decals under all others and their textures among
        /// its files; the document itself when it has none.
        /// </summary>
        /// <param name="readFile">Reads a slot's file by its path as the slot names it (relative to
        /// the project, or absolute); null when it cannot. Paths into the map's own folder are read
        /// from the document.</param>
        /// <param name="written">What became of each decal.</param>
        /// <exception cref="MapProjectException">A chosen file cannot be read or is no picture.</exception>
        public static MapDocument Apply(MapDocument document, Func<string, byte[]?> readFile, out IReadOnlyList<MapWideWritten> written, CancellationToken cancel = default)
        {
            List<MapWideWritten> made = [];
            written = made;
            MapWideDecals wide = document.WideDecals;
            if (wide.Albedo.Source == MapWideSource.None && wide.Normal.Source == MapWideSource.None && wide.Shadow.Source == MapWideSource.None)
            {
                return document;
            }

            ImmutableSortedDictionary<string, ImmutableArray<byte>> files = document.Files;
            List<Decal> decals = [];
            int nextId = document.Decals.IsEmpty ? 0 : document.Decals.Max(decal => decal.Id) + 1;
            Heightmap heights = document.Terrain.Heightmap;
            Vector3 corner = new Vector3(0, heights.HeightAt(0, 0), 0);
            Vector3 scale = new Vector3(document.Width, document.Width, document.Height);

            Decal Wide(DecalType type, string first, string second) => new Decal
            {
                Id = nextId++,
                Type = type,
                Textures = [first, second],
                Scale = scale,
                Position = corner,
                Rotation = Vector3.Zero,
                CutOffLod = CutOffLod,
                NearCutOffLod = 0,
                OwnerArmy = -1,
            };

            // a texture as the map names it: the map's own file as it is, or a file written into the map
            string Texture(MapWideKind kind, string name, Func<byte[]> make, string how)
            {
                cancel.ThrowIfCancellationRequested();
                string path = FreeName(files, $"{Folder}/{name}.dds");
                files = files.SetItem(path, ImmutableCollectionsMarshal.AsImmutableArray(make()));
                made.Add(new MapWideWritten(kind, path, how));
                return MapPath(document, path);
            }

            string FromFile(MapWideKind kind, string file, string name, DdsCompression compression)
            {
                if (OwnFile(document, file) is string own)
                {
                    if (!files.ContainsKey(own))
                    {
                        throw new MapProjectException($"The map-wide {Title(kind)} names {file}, which the map's own files lack");
                    }
                    made.Add(new MapWideWritten(kind, own, "the map's own file"));
                    return MapPath(document, own);
                }
                byte[] bytes = readFile(file) ?? throw new MapProjectException($"The map-wide {Title(kind)}'s file {file} cannot be read");
                bool dds = bytes.Length >= 4 && bytes[0] == 'D' && bytes[1] == 'D' && bytes[2] == 'S';
                return Texture(kind, name, () => dds ? bytes : Convert(bytes, file, compression), dds ? $"copied from {file}" : $"converted from {file}");
            }

            if (wide.Albedo is { Source: MapWideSource.File, File: string albedo })
            {
                string texture = FromFile(MapWideKind.Albedo, albedo, "map_albedo", DdsCompression.Dxt5);
                string specular = wide.Specular is string spec ? FromFile(MapWideKind.Albedo, spec, "map_specular", DdsCompression.Dxt1) : "";
                decals.Add(Wide(DecalType.Albedo, texture, specular));
            }
            if (wide.Shadow.Source != MapWideSource.None)
            {
                string texture = wide.Shadow is { Source: MapWideSource.File, File: string shadow }
                    ? FromFile(MapWideKind.Shadow, shadow, "map_lighting", DdsCompression.Dxt5)
                    : Texture(MapWideKind.Shadow, "map_lighting", () => Dds(GenerateShadow(document, cancel: cancel)), "generated");
                decals.Add(Wide(DecalType.Albedo, texture, ""));
            }
            if (wide.Normal.Source != MapWideSource.None)
            {
                string texture = wide.Normal is { Source: MapWideSource.File, File: string normals }
                    ? FromFile(MapWideKind.Normal, normals, "map_normals", DdsCompression.Dxt5)
                    : Texture(MapWideKind.Normal, "map_normals", () => Dds(GenerateNormals(document, cancel: cancel)), "generated");
                decals.Add(Wide(DecalType.Normals, texture, ""));
            }

            return document with { Decals = [.. decals, .. document.Decals], Files = files };
        }

        /// <summary>
        /// The generated normals decal of a map at <paramref name="pixelsPerOgrid"/> (16 at export),
        /// at most <paramref name="largest"/> on a side.
        /// </summary>
        public static DecodedTexture GenerateNormals(MapDocument document, int pixelsPerOgrid = MapWideDecalImages.DetailPixelsPerOgrid, int largest = MapWideDecalImages.LargestSide, CancellationToken cancel = default)
        {
            (int width, int height) = MapWideDecalImages.SizeOf(document.Width, document.Height, pixelsPerOgrid, largest);
            return MapWideDecalImages.Normals(document.Terrain.Heightmap, width, height, cancel);
        }

        /// <summary>
        /// The generated shadow decal of a map at <paramref name="pixelsPerOgrid"/> (4 at export), at
        /// most <paramref name="largest"/> on a side.
        /// </summary>
        public static DecodedTexture GenerateShadow(MapDocument document, int pixelsPerOgrid = MapWideDecalImages.ShadowPixelsPerOgrid, int largest = MapWideDecalImages.LargestSide, CancellationToken cancel = default)
        {
            (int width, int height) = MapWideDecalImages.SizeOf(document.Width, document.Height, pixelsPerOgrid, largest);
            return MapWideDecalImages.Shadow(document.Terrain.Heightmap, document.WideDecals.ShadowSettings, document.Lighting.SunDirection, width, height, cancel);
        }

        /// <summary>
        /// The path in the map's folder a slot's file names when it is one of the map's own files
        /// (<c>/maps/&lt;folder&gt;/env/decals/a.dds</c> is <c>env/decals/a.dds</c>); null for a file on disk.
        /// </summary>
        public static string? OwnFile(MapDocument document, string file)
        {
            string key = file.Replace('\\', '/').TrimStart('/');
            if (!key.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            int folderEnd = key.IndexOf('/', "maps/".Length);
            string inFolder = folderEnd < 0 ? "" : key[(folderEnd + 1)..];
            return document.Files.Keys.FirstOrDefault(path => string.Equals(path, inFolder, StringComparison.OrdinalIgnoreCase)) ?? inFolder;
        }

        private static string Title(MapWideKind kind) => kind switch
        {
            MapWideKind.Albedo => "albedo",
            MapWideKind.Normal => "normals decal",
            _ => "shadow decal",
        };

        private static string MapPath(MapDocument document, string path) => $"/maps/{document.Folder}/{path}";

        private static string FreeName(ImmutableSortedDictionary<string, ImmutableArray<byte>> files, string path)
        {
            string stem = path[..^4];
            string free = path;
            for (int n = 2; files.Keys.Any(file => string.Equals(file, free, StringComparison.OrdinalIgnoreCase)); n++)
            {
                free = $"{stem}_{n}.dds";
            }
            return free;
        }

        private static byte[] Dds(DecodedTexture picture) =>
            DdsTexture.WriteMipmapped(picture.Width, picture.Height, DdsCompression.Dxt5, picture.Rgba);

        /// <summary>
        /// A picture of any format ImageMagick reads as a DDS with mipmaps, made smaller when a side
        /// is beyond <see cref="MapWideDecalImages.LargestSide"/>.
        /// </summary>
        private static byte[] Convert(byte[] bytes, string file, DdsCompression compression)
        {
            try
            {
                using MagickImage image = new MagickImage(bytes);
                uint longer = Math.Max(image.Width, image.Height);
                if (longer > MapWideDecalImages.LargestSide)
                {
                    image.Resize(new MagickGeometry(MapWideDecalImages.LargestSide, MapWideDecalImages.LargestSide));
                }
                image.Alpha(AlphaOption.Set);
                using IPixelCollection<ushort> pixels = image.GetPixels();
                return DdsTexture.WriteMipmapped((int)image.Width, (int)image.Height, compression, pixels.ToByteArray(PixelMapping.RGBA)!);
            }
            catch (MagickException exception)
            {
                throw new MapProjectException($"The map-wide decal's file {file} is no picture: {exception.Message}");
            }
        }
    }
}
