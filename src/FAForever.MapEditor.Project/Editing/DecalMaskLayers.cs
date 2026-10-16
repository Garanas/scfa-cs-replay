using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// A greyscale picture: one byte per pixel, the first row first.
    /// </summary>
    public sealed record MaskPicture(int Width, int Height, byte[] Pixels)
    {
        /// <summary>
        /// The picture's value at (u, v), 0 to 1 over its width and height, between the centres of
        /// its pixels; 0 outside.
        /// </summary>
        public float Sample(float u, float v)
        {
            if (u < 0 || v < 0 || u > 1 || v > 1)
            {
                return 0;
            }
            float x = u * Width - 0.5f;
            float y = v * Height - 0.5f;
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float a = x - x0, b = y - y0;
            int i0 = Math.Clamp(x0, 0, Width - 1), i1 = Math.Clamp(x0 + 1, 0, Width - 1);
            int j0 = Math.Clamp(y0, 0, Height - 1), j1 = Math.Clamp(y0 + 1, 0, Height - 1);
            float top = Pixels[j0 * Width + i0] * (1 - a) + Pixels[j0 * Width + i1] * a;
            float bottom = Pixels[j1 * Width + i0] * (1 - a) + Pixels[j1 * Width + i1] * a;
            return (top * (1 - b) + bottom * b) / 255f;
        }

        /// <summary>
        /// The grey of a picture's red, green and blue (their mean), as decal masks are read.
        /// </summary>
        public static MaskPicture Of(DecodedTexture texture)
        {
            byte[] pixels = new byte[texture.Width * texture.Height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (byte)((texture.Rgba[i * 4] + texture.Rgba[i * 4 + 1] + texture.Rgba[i * 4 + 2] + 1) / 3);
            }
            return new MaskPicture(texture.Width, texture.Height, pixels);
        }
    }

    /// <summary>
    /// Reads the mask pictures of decal sets by their game paths; the editor's reads the workspace's
    /// assets and the map's own files, tests give pictures of their own.
    /// </summary>
    public interface IDecalMaskPictures
    {
        /// <summary>
        /// The picture of a mask, or null when there is none.
        /// </summary>
        MaskPicture? Read(string gamePath, MapDocument document);
    }

    /// <summary>
    /// Mask pictures found as the game finds textures (<see cref="GameTextures.Find"/>: the map's own
    /// files, else the workspace's converted assets), read once per version of their file.
    /// </summary>
    public sealed class GameMaskPictures(Func<Workspace?> workspace) : IDecalMaskPictures
    {
        private readonly ConcurrentDictionary<string, (object Version, MaskPicture? Picture)> _read = new ConcurrentDictionary<string, (object, MaskPicture?)>(StringComparer.Ordinal);

        public MaskPicture? Read(string gamePath, MapDocument document)
        {
            if (GameTextures.Find(gamePath, document, workspace()) is not TextureSource source)
            {
                return null;
            }
            if (_read.TryGetValue(source.Key, out (object Version, MaskPicture? Picture) held) && Equals(held.Version, source.Version))
            {
                return held.Picture;
            }
            MaskPicture? picture;
            try
            {
                picture = MaskPicture.Of(source.Read());
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ImageMagick.MagickException)
            {
                picture = null;
            }
            _read[source.Key] = (source.Version, picture);
            return picture;
        }
    }

    /// <summary>
    /// The decal-masks layers of the map's stacks (<see cref="DecalMasksSource"/>): their grids drawn
    /// from the masks of the placed decal sets, and drawn again where decals moved, turned, were
    /// sized, added or removed, so a stratum or mask that follows a rock outcrop's mask moves with
    /// the rock.
    /// </summary>
    /// <remarks>
    /// Each pixel (each corner in a stack of heights) takes, of every decal of the layer's sets that
    /// covers it, the decal's mask at that place (the mask stretched over the decal's rectangle, its
    /// first row along the decal's own z from its corner), the highest where decals overlap. A mask
    /// that cannot be read draws nothing.
    /// </remarks>
    public static class DecalMaskLayers
    {
        private const int Size = TiledBuffer<byte>.TileSize;

        /// <summary>
        /// The stacks of a document with how to put a changed one back: the heightmap's and every
        /// stack of coverage (strata, masks, water maps), whose composites <see cref="CoverageLayers.Follow"/>
        /// then makes again where a layer's grid changed.
        /// </summary>
        private static IEnumerable<(LayerStack Stack, Func<MapDocument, LayerStack, MapDocument> Put)> StacksOf(MapDocument document)
        {
            yield return (document.HeightStack, (d, stack) => d with { HeightStack = stack });
            foreach ((string key, LayerStack stack) in document.CoverageStacks)
            {
                yield return (stack, (d, changed) => d with { CoverageStacks = d.CoverageStacks.SetItem(key, changed) });
            }
        }

        /// <summary>
        /// The document with every decal-masks layer of its stacks drawn again where the decals
        /// changed since <paramref name="previous"/> (all of it when the grid's size changed); the
        /// document itself when no such layer needs it, or without pictures to read.
        /// </summary>
        public static MapDocument Follow(MapDocument previous, MapDocument next, IDecalMaskPictures? pictures)
        {
            if (pictures is null || previous.Decals == next.Decals)
            {
                return next;
            }
            MapDocument result = next;
            foreach ((LayerStack stack, Func<MapDocument, LayerStack, MapDocument> put) in StacksOf(next))
            {
                LayerStack followed = Follow(previous, result, stack, pictures);
                if (!ReferenceEquals(followed, stack))
                {
                    result = put(result, followed);
                }
            }
            return result;
        }

        /// <summary>
        /// A stack with its decal-masks layers drawn again where the decals changed between the two
        /// documents; the stack itself when nothing changed for it.
        /// </summary>
        public static LayerStack Follow(MapDocument previous, MapDocument next, LayerStack stack, IDecalMaskPictures pictures)
        {
            if (!stack.Layers.Any(layer => layer.Source is DecalMasksSource) || previous.Decals == next.Decals)
            {
                return stack;
            }
            StackGrid grid = LayerStacks.GridOf(next, stack);
            // a changed decal is a new object (versions share what they do not change), and ids need
            // not be unique in a map: the decals of either version the other lacks are what changed
            HashSet<Decal> before = new HashSet<Decal>(previous.Decals, ReferenceEqualityComparer.Instance);
            HashSet<Decal> after = new HashSet<Decal>(next.Decals, ReferenceEqualityComparer.Instance);
            List<Decal> changed = [.. next.Decals.Where(decal => !before.Contains(decal)), .. previous.Decals.Where(decal => !after.Contains(decal))];
            if (changed.Count == 0)
            {
                return stack;
            }

            ImmutableArray<Layer> layers = stack.Layers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].Source is not DecalMasksSource source)
                {
                    continue;
                }
                List<Decal> touched = [.. changed.Where(decal => decal.Set?.Masks.ContainsKey(source.Mask) == true)];
                if (touched.Count == 0)
                {
                    continue;
                }
                bool[] dirty = new bool[grid.TilesX * grid.TilesY];
                foreach (Decal decal in touched)
                {
                    MarkTiles(dirty, grid, decal);
                }
                layers = layers.SetItem(i, Draw(next, stack.Target.IsHeights, grid, layers[i], source, pictures, dirty));
            }
            return layers.AsSpan().SequenceEqual(stack.Layers.AsSpan()) ? stack : stack with { Layers = layers };
        }

        /// <summary>
        /// A new decal-masks layer of a stack, drawn: in a stack of heights it adds (its height times
        /// the masks), in a stack of coverage it keeps the higher of its masks and the layers below.
        /// </summary>
        public static Layer New(MapDocument document, LayerStack stack, string name, DecalMasksSource source, IDecalMaskPictures pictures) =>
            Draw(document, stack, new Layer
            {
                Name = name,
                Source = source,
                Combine = stack.Target.IsHeights ? LayerCombine.Add : LayerCombine.Maximum,
            }, pictures);

        /// <summary>
        /// A stack with a decal-masks layer's source changed (another mask name, other sets, another
        /// height) and the layer drawn again.
        /// </summary>
        /// <exception cref="ArgumentException">The stack has no decal-masks layer of that name.</exception>
        public static LayerStack SetSource(MapDocument document, LayerStack stack, string name, DecalMasksSource source, IDecalMaskPictures pictures)
        {
            int index = stack.IndexOf(name);
            if (index < 0 || stack.Layers[index].Source is not DecalMasksSource)
            {
                throw new ArgumentException($"There is no decal-masks layer {name}");
            }
            Layer layer = stack.Layers[index];
            if (layer.Source.Equals(source))
            {
                return stack;
            }
            return stack.With(index, Draw(document, stack, layer with { Source = source }, pictures));
        }

        /// <summary>
        /// The names of the masks the map's decal sets have (<c>main</c>, <c>moss</c>, ...), sorted.
        /// </summary>
        public static IReadOnlyList<string> MaskNamesOf(MapDocument document) =>
            [.. document.Decals.Where(decal => decal.Set is not null).SelectMany(decal => decal.Set!.Masks.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        /// <summary>
        /// A decal-masks layer with its grid drawn from the decals of a document: over the tiles
        /// marked in <paramref name="dirty"/> (all of them when null or when the layer has no grid
        /// of the stack's size yet), keeping tiles that come out the same.
        /// </summary>
        public static Layer Draw(MapDocument document, LayerStack stack, Layer layer, IDecalMaskPictures pictures, bool[]? dirty = null)
        {
            if (layer.Source is not DecalMasksSource source)
            {
                throw new ArgumentException($"The layer {layer.Name} does not follow decal masks", nameof(layer));
            }
            return Draw(document, stack.Target.IsHeights, LayerStacks.GridOf(document, stack), layer, source, pictures, dirty);
        }

        private static Layer Draw(MapDocument document, bool heights, StackGrid grid, Layer layer, DecalMasksSource source, IDecalMaskPictures pictures, bool[]? dirty)
        {

            // what draws: the decals of the layer's sets, with their masks read and their rectangles
            List<(Vector2 Corner, Vector2 U, Vector2 V, MaskPicture Picture, Vector4 Box)> decals = [];
            foreach ((Decal decal, string path) in DecalSets.MasksOf(document, source.Mask, source.Sets))
            {
                if (pictures.Read(path, document) is MaskPicture picture)
                {
                    (Vector2 u, Vector2 v) = MapObjects.DecalEdges(decal.Scale, decal.Rotation.Y);
                    Vector2 corner = new Vector2(decal.Position.X, decal.Position.Z);
                    decals.Add((corner, u, v, picture, BoxOf(corner, u, v)));
                }
            }

            if (heights)
            {
                TiledBuffer<float>? current = layer.Values is { } values && values.Width == grid.Width && values.Height == grid.Height ? values : null;
                TiledBuffer<float>.Builder builder = (current ?? TiledBuffer<float>.Create(grid.Width, grid.Height)).ToBuilder();
                bool[] all = dirty is not null && current is not null ? dirty : Enumerable.Repeat(true, grid.TilesX * grid.TilesY).ToArray();
                float height = source.Height;
                DrawTiles(grid, all, decals, (tx, ty, coverage) =>
                {
                    float[] tile = new float[Size * Size];
                    for (int i = 0; i < tile.Length; i++)
                    {
                        tile[i] = coverage[i] * height;
                    }
                    if (current is null || !current.Tile(tx, ty).SequenceEqual(tile))
                    {
                        lock (builder)
                        {
                            builder.SetTile(tx, ty, tile);
                        }
                    }
                });
                TiledBuffer<float> made = builder.ToBuffer();
                return ReferenceEquals(made, current) ? layer : layer with { Values = made, Coverage = null };
            }
            else
            {
                TiledBuffer<byte>? current = layer.Coverage is { } coverage && coverage.Width == grid.Width && coverage.Height == grid.Height ? coverage : null;
                TiledBuffer<byte>.Builder builder = (current ?? TiledBuffer<byte>.Create(grid.Width, grid.Height)).ToBuilder();
                bool[] all = dirty is not null && current is not null ? dirty : Enumerable.Repeat(true, grid.TilesX * grid.TilesY).ToArray();
                DrawTiles(grid, all, decals, (tx, ty, values) =>
                {
                    byte[] tile = new byte[Size * Size];
                    for (int i = 0; i < tile.Length; i++)
                    {
                        tile[i] = (byte)Math.Clamp(MathF.Round(values[i] * 255), 0, 255);
                    }
                    if (current is null || !current.Tile(tx, ty).SequenceEqual(tile))
                    {
                        lock (builder)
                        {
                            builder.SetTile(tx, ty, tile);
                        }
                    }
                });
                TiledBuffer<byte> made = builder.ToBuffer();
                return ReferenceEquals(made, current) ? layer : layer with { Coverage = made, Values = null };
            }
        }

        /// <summary>
        /// Works out the dirty tiles' values (0 to 1) on the pool and hands each tile over.
        /// </summary>
        private static void DrawTiles(StackGrid grid, bool[] dirty, List<(Vector2 Corner, Vector2 U, Vector2 V, MaskPicture Picture, Vector4 Box)> decals, Action<int, int, float[]> done)
        {
            Parallel.For(0, dirty.Length, t =>
            {
                if (!dirty[t])
                {
                    return;
                }
                int tx = t % grid.TilesX, ty = t / grid.TilesX;
                float[] values = new float[Size * Size];
                int columns = Math.Min(Size, grid.Width - tx * Size);
                int rows = Math.Min(Size, grid.Height - ty * Size);
                // the tile's extent on the map, to pass decals that miss it
                float x0 = grid.X(tx * Size), x1 = grid.X(tx * Size + columns - 1);
                float z0 = grid.Z(ty * Size), z1 = grid.Z(ty * Size + rows - 1);
                foreach ((Vector2 corner, Vector2 u, Vector2 v, MaskPicture picture, Vector4 box) in decals)
                {
                    if (box.X > x1 || box.Z < x0 || box.Y > z1 || box.W < z0)
                    {
                        continue;
                    }
                    float uu = u.LengthSquared(), vv = v.LengthSquared();
                    if (uu <= 0 || vv <= 0)
                    {
                        continue;
                    }
                    for (int row = 0; row < rows; row++)
                    {
                        float z = grid.Z(ty * Size + row);
                        if (z < box.Y || z > box.W)
                        {
                            continue;
                        }
                        for (int column = 0; column < columns; column++)
                        {
                            float x = grid.X(tx * Size + column);
                            if (x < box.X || x > box.Z)
                            {
                                continue;
                            }
                            Vector2 d = new Vector2(x, z) - corner;
                            float value = picture.Sample(Vector2.Dot(d, u) / uu, Vector2.Dot(d, v) / vv);
                            int at = row * Size + column;
                            if (value > values[at])
                            {
                                values[at] = value;
                            }
                        }
                    }
                }
                done(tx, ty, values);
            });
        }

        /// <summary>
        /// The bounds of a decal's rectangle on the map: least x, least z, most x, most z.
        /// </summary>
        private static Vector4 BoxOf(Vector2 corner, Vector2 u, Vector2 v)
        {
            Vector2 a = corner, b = corner + u, c = corner + v, d = corner + u + v;
            return new Vector4(
                MathF.Min(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X)),
                MathF.Min(MathF.Min(a.Y, b.Y), MathF.Min(c.Y, d.Y)),
                MathF.Max(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X)),
                MathF.Max(MathF.Max(a.Y, b.Y), MathF.Max(c.Y, d.Y)));
        }

        private static void MarkTiles(bool[] dirty, StackGrid grid, Decal decal)
        {
            (Vector2 u, Vector2 v) = MapObjects.DecalEdges(decal.Scale, decal.Rotation.Y);
            Vector4 box = BoxOf(new Vector2(decal.Position.X, decal.Position.Z), u, v);
            (int fromColumn, int toColumn) = grid.ColumnsOf(box.X, box.Z);
            (int fromRow, int toRow) = grid.RowsOf(box.Y, box.W);
            for (int ty = Math.Max(0, fromRow / Size); ty <= Math.Min(grid.TilesY - 1, toRow / Size); ty++)
            {
                for (int tx = Math.Max(0, fromColumn / Size); tx <= Math.Min(grid.TilesX - 1, toColumn / Size); tx++)
                {
                    dirty[ty * grid.TilesX + tx] = true;
                }
            }
        }
    }
}
