using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Where the old map lies in the new one when it grows or shrinks: at a corner, the middle of
    /// a side, or the centre. Top is north (<c>z = 0</c>), as the top view shows it.
    /// </summary>
    public enum MapAnchor
    {
        TopLeft,
        Top,
        TopRight,
        Left,
        Centre,
        Right,
        BottomLeft,
        Bottom,
        BottomRight,
    }

    /// <summary>
    /// How a map grows or shrinks (user story M-3).
    /// </summary>
    public sealed record MapResizeSettings
    {
        /// <summary>
        /// The new width in ogrids, one of the game's sizes (<see cref="NewMap.Sizes"/>).
        /// </summary>
        public required int Width { get; init; }

        /// <summary>
        /// The new height in ogrids, one of the game's sizes; it may differ from the width (a map of
        /// 5 by 10 km is 256 by 512 ogrids).
        /// </summary>
        public required int Height { get; init; }

        /// <summary>
        /// Where the old map lies in the new one, unless <see cref="Offset"/> says.
        /// </summary>
        public MapAnchor Anchor { get; init; } = MapAnchor.Centre;

        /// <summary>
        /// Where the old map's corner (0, 0) lands in the new map, in whole ogrids; negative crops
        /// from the top or left. Given, it wins over <see cref="Anchor"/>.
        /// </summary>
        public (int X, int Z)? Offset { get; init; }

        /// <summary>
        /// The height of the terrain the map gains, in game units; null continues the heights at
        /// the old map's edges outwards.
        /// </summary>
        public float? FillHeight { get; init; }

        /// <summary>
        /// Whether the playable area becomes the whole new map; otherwise it moves with the rest.
        /// </summary>
        public bool FitPlayableArea { get; init; }
    }

    /// <summary>
    /// What a resize took out of the map: the things that lie outside the new map.
    /// </summary>
    public sealed record MapResizeRemoved(int Props, int Decals, int Units, int Markers, int Areas, int WaveGenerators)
    {
        public int Total => Props + Decals + Units + Markers + Areas + WaveGenerators;

        /// <summary>
        /// E.g. <c>12 props, 3 decals</c>; empty when nothing was removed.
        /// </summary>
        public override string ToString() => string.Join(", ", new (int Count, string One, string Many)[]
            {
                (Props, "prop", "props"), (Decals, "decal", "decals"), (Units, "unit", "units"), (Markers, "marker", "markers"),
                (Areas, "area", "areas"), (WaveGenerators, "wave generator", "wave generators"),
            }
            .Where(part => part.Count > 0)
            .Select(part => $"{part.Count} {(part.Count == 1 ? part.One : part.Many)}"));
    }

    /// <summary>
    /// A resized map and what happened on the way.
    /// </summary>
    /// <param name="OffsetX">Where the old map's corner (0, 0) lies in the new one, in ogrids.</param>
    /// <param name="SpawnsMoved">Spawns that lay outside the new map, moved to its nearest edge: a
    /// playable army keeps its spawn.</param>
    /// <param name="AreasClipped">Areas that stuck out of the new map and were cut to it.</param>
    public sealed record MapResizeResult(MapDocument Document, int OffsetX, int OffsetZ, MapResizeRemoved Removed, int SpawnsMoved, int AreasClipped);

    /// <summary>
    /// Grows or crops a map to another of the game's sizes (user story M-3): every grid of the
    /// document and every thing on it follows the old map to its place in the new one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The grids keep their size relative to the map (masks of half the map's size stay half): the
    /// heightmap (what the map gains takes the edge's height or <see cref="MapResizeSettings.FillHeight"/>),
    /// the terrain types, the stratum masks, the water's masks and map, the normal map (out of date
    /// afterwards, as its generator tells: the heightmap changed) and the masks for tools (empty
    /// where the map gains); the other grids continue their edges. The preview keeps its 256 pixels
    /// and is out of date. A new grid of the document gets its line in <see cref="Resize"/>, with
    /// <see cref="Grid{T}"/>.
    /// </para>
    /// <para>
    /// Things move by the offset and keep their height (the terrain under them moved with them).
    /// What lies outside the new map is removed (props, decals by their centre, units, markers with
    /// their links and chain entries, wave generators), except spawns, which move to the nearest
    /// edge, so that no army loses its spawn. Areas are cut to the new map, and removed when nothing
    /// of them is left.
    /// </para>
    /// </remarks>
    public static class MapResize
    {
        /// <summary>
        /// Where the old map's corner (0, 0) lands in the new map, in ogrids.
        /// </summary>
        public static (int X, int Z) OffsetOf(int width, int height, MapResizeSettings settings)
        {
            if (settings.Offset is (int x, int z))
            {
                return (x, z);
            }
            int dx = settings.Width - width;
            int dz = settings.Height - height;
            int column = (int)settings.Anchor % 3;
            int row = (int)settings.Anchor / 3;
            return (column == 0 ? 0 : column == 1 ? dx / 2 : dx, row == 0 ? 0 : row == 1 ? dz / 2 : dz);
        }

        /// <summary>
        /// The map at the new size.
        /// </summary>
        /// <exception cref="ArgumentException">A size is none of the game's, or the fill height lies
        /// outside what the heightmap holds.</exception>
        public static MapResizeResult Resize(MapDocument document, MapResizeSettings settings)
        {
            if (!NewMap.Sizes.Contains(settings.Width) || !NewMap.Sizes.Contains(settings.Height))
            {
                throw new ArgumentException($"A map's side is {string.Join(", ", NewMap.Sizes)} ogrids, not {settings.Width} by {settings.Height}", nameof(settings));
            }
            Heightmap heightmap = document.Terrain.Heightmap;
            ushort? fill = null;
            if (settings.FillHeight is float height)
            {
                float highest = ushort.MaxValue * heightmap.Scale;
                if (height < 0 || height > highest)
                {
                    throw new ArgumentException($"Heights lie between 0 and {highest:0}", nameof(settings));
                }
                fill = (ushort)Math.Clamp(MathF.Round(height / heightmap.Scale), 0, ushort.MaxValue);
            }

            int width = document.Width;
            int depth = document.Height;
            int newWidth = settings.Width;
            int newDepth = settings.Height;
            (int dx, int dz) = OffsetOf(width, depth, settings);

            TiledBuffer<T> Resized<T>(TiledBuffer<T> grid, T? constant = null) where T : unmanaged =>
                Grid(grid, width, depth, newWidth, newDepth, dx, dz, constant);

            Terrain terrain = document.Terrain;
            Water water = document.Water;
            MapDocument resized = document with
            {
                Terrain = terrain with
                {
                    Heightmap = heightmap with { Samples = Shift(heightmap.Samples, newWidth + 1, newDepth + 1, dx, dz, fill) },
                    Types = Resized(terrain.Types),
                    StratumMasks = terrain.StratumMasks with { Layers = [.. terrain.StratumMasks.Layers.Select(layer => Resized(layer))] },
                    NormalMap = Resized(terrain.NormalMap),
                },
                Water = water with
                {
                    Map = Resized(water.Map),
                    FoamMask = Resized(water.FoamMask),
                    FlatnessMask = Resized(water.FlatnessMask),
                    DepthBiasMask = Resized(water.DepthBiasMask),
                },
                Masks = document.Masks.ToImmutableSortedDictionary(entry => entry.Key, entry => Resized(entry.Value, (byte)0), StringComparer.Ordinal),
            };

            return MoveThings(resized, document, dx, dz, settings.FitPlayableArea);
        }

        /// <summary>
        /// A grid of the map at the new size: it keeps its size relative to the map (a grid of half
        /// the map's size stays half), the old values at their place, the rest <paramref name="fill"/>
        /// or, when null, the nearest value at the old grid's edge. An offset that falls between two
        /// pixels of a smaller grid is rounded down.
        /// </summary>
        /// <param name="mapWidth">The old map's width in ogrids.</param>
        /// <param name="offsetX">Where the old map's corner lands in the new one, in ogrids.</param>
        public static TiledBuffer<T> Grid<T>(TiledBuffer<T> grid, int mapWidth, int mapHeight, int newWidth, int newHeight, int offsetX, int offsetZ, T? fill = null) where T : unmanaged
        {
            double scaleX = (double)grid.Width / mapWidth;
            double scaleZ = (double)grid.Height / mapHeight;
            int width = Math.Max(1, (int)Math.Round(newWidth * scaleX));
            int height = Math.Max(1, (int)Math.Round(newHeight * scaleZ));
            return Shift(grid, width, height, (int)Math.Floor(offsetX * scaleX), (int)Math.Floor(offsetZ * scaleZ), fill);
        }

        /// <summary>
        /// The values of <paramref name="grid"/> in a grid of another size, moved by an offset in
        /// its own pixels; the rest <paramref name="fill"/>, or the nearest edge value when null.
        /// </summary>
        public static TiledBuffer<T> Shift<T>(TiledBuffer<T> grid, int width, int height, int offsetX, int offsetY, T? fill = null) where T : unmanaged
        {
            if (width == grid.Width && height == grid.Height && offsetX == 0 && offsetY == 0)
            {
                return grid;
            }
            T[] source = grid.ToArray();
            T[] values = new T[width * height];
            int sourceWidth = grid.Width;
            int sourceHeight = grid.Height;
            int x0 = Math.Clamp(offsetX, 0, width);
            int x1 = Math.Clamp(offsetX + sourceWidth, 0, width);
            for (int y = 0; y < height; y++)
            {
                Span<T> row = values.AsSpan(y * width, width);
                int sy = y - offsetY;
                if ((sy < 0 || sy >= sourceHeight) && fill is T constant)
                {
                    row.Fill(constant);
                    continue;
                }
                ReadOnlySpan<T> from = source.AsSpan(Math.Clamp(sy, 0, sourceHeight - 1) * sourceWidth, sourceWidth);
                if (x1 > x0)
                {
                    from.Slice(x0 - offsetX, x1 - x0).CopyTo(row[x0..x1]);
                }
                row[..x0].Fill(fill ?? from[0]);
                row[x1..].Fill(fill ?? from[^1]);
            }
            return TiledBuffer<T>.FromArray(width, height, values);
        }

        /// <summary>
        /// Moves the things of the map by the offset and takes out what lies outside it.
        /// </summary>
        private static MapResizeResult MoveThings(MapDocument document, MapDocument before, int dx, int dz, bool fitPlayableArea)
        {
            int width = document.Width;
            int height = document.Height;
            Vector3 offset = new Vector3(dx, 0, dz);
            bool Inside(Vector3 place) => place.X >= 0 && place.X <= width && place.Z >= 0 && place.Z <= height;

            // props, decals, wave generators: moved, and out when outside
            ImmutableArray<Prop> props = [.. document.Props.Select(prop => prop with { Position = prop.Position + offset }).Where(prop => Inside(prop.Position))];
            ImmutableArray<Decal> decals = [.. document.Decals.Select(decal => decal with { Position = decal.Position + offset }).Where(decal => Inside(MapObjects.DecalCentre(decal)))];
            HashSet<int> keptDecals = [.. decals.Select(decal => decal.Id)];
            ImmutableArray<DecalGroup> groups = [.. document.DecalGroups.Select(group => group.DecalIds.All(keptDecals.Contains) ? group : group with { DecalIds = [.. group.DecalIds.Where(keptDecals.Contains)] })];
            ImmutableArray<WaveGenerator> waves = [.. document.Water.WaveGenerators.Select(wave => wave with { Position = wave.Position + offset }).Where(wave => Inside(wave.Position))];

            // units: moved, and out when outside
            int removedUnits = 0;
            UnitGroup Move(UnitGroup group)
            {
                ImmutableArray<Unit> units = [.. group.Units.Select(unit => unit with { Position = unit.Position + offset }).Where(unit => Inside(unit.Position))];
                removedUnits += group.Units.Length - units.Length;
                return group with { Units = units, Groups = [.. group.Groups.Select(Move)] };
            }
            ImmutableArray<Army> armies = [.. document.Armies.Select(army => army with { Units = Move(army.Units) })];

            // markers: moved; spawns kept inside, the others out when outside (with their links)
            int spawnsMoved = 0;
            Spawn Keep(Spawn spawn)
            {
                Vector3 place = spawn.Position + offset;
                if (Inside(place))
                {
                    return spawn with { Position = place };
                }
                spawnsMoved++;
                return spawn with { Position = new Vector3(Math.Clamp(place.X, 0.5f, width - 0.5f), place.Y, Math.Clamp(place.Z, 0.5f, height - 0.5f)) };
            }
            ImmutableArray<T> Moved<T>(ImmutableArray<T> markers) where T : Marker => [.. markers.Select(marker => (T)((Marker)marker with { Position = marker.Position + offset }))];
            MarkerSet set = document.Markers;
            set = set with
            {
                Spawns = [.. set.Spawns.Select(Keep)],
                Resources = Moved(set.Resources),
                PathNodes = Moved(set.PathNodes),
                AiMarkers = Moved(set.AiMarkers),
                WeatherDefinitions = Moved(set.WeatherDefinitions),
                WeatherGenerators = Moved(set.WeatherGenerators),
            };

            // areas: moved and cut to the map
            int clipped = 0;
            ImmutableArray<Area>.Builder areas = ImmutableArray.CreateBuilder<Area>();
            List<string> emptied = [];
            string? playable = before.PlayableArea?.Name;
            foreach (Area area in set.Areas)
            {
                Area shifted = fitPlayableArea && area.Name == playable
                    ? area with { X0 = 0, Z0 = 0, X1 = width, Z1 = height }
                    : area with { X0 = area.X0 + dx, X1 = area.X1 + dx, Z0 = area.Z0 + dz, Z1 = area.Z1 + dz };
                Area cut = shifted with
                {
                    X0 = Math.Clamp(shifted.X0, 0, width),
                    X1 = Math.Clamp(shifted.X1, 0, width),
                    Z0 = Math.Clamp(shifted.Z0, 0, height),
                    Z1 = Math.Clamp(shifted.Z1, 0, height),
                };
                if (cut != shifted)
                {
                    clipped++;
                }
                areas.Add(cut);
                if (cut.X1 - cut.X0 <= 0 || cut.Z1 - cut.Z0 <= 0)
                {
                    emptied.Add(cut.Name);
                }
            }
            set = set with { Areas = areas.ToImmutable() };

            Sky sky = document.Sky;
            if (sky.Skybox is Skybox skybox)
            {
                // the map editors put the sky dome over the map's centre
                bool centred = Math.Abs(skybox.Position.X - before.Width / 2f) <= 1 && Math.Abs(skybox.Position.Z - before.Height / 2f) <= 1;
                Vector3 place = centred ? new Vector3(width / 2f, skybox.Position.Y, height / 2f) : skybox.Position + offset;
                sky = sky with { Skybox = skybox with { Position = place } };
            }

            MapDocument moved = document with
            {
                Props = props,
                Decals = decals,
                DecalGroups = groups,
                Armies = armies,
                Markers = set,
                Water = document.Water with { WaveGenerators = waves },
                Sky = sky,
            };

            // markers outside go with every link and chain entry to them
            ObjectRef[] outside = [.. moved.Markers.All.Where(marker => marker is not Spawn && !Inside(marker.Position)).Select(marker => ObjectRef.Marker(marker.Id))];
            moved = moved.RemoveObjects(outside);
            foreach (string name in emptied)
            {
                moved = moved.RemoveArea(name);
            }
            if (fitPlayableArea && moved.Markers.Areas.IsEmpty)
            {
                moved = moved.AddArea(new Area { Name = "AREA_1", X0 = 0, Z0 = 0, X1 = width, Z1 = height });
            }

            MapResizeRemoved removed = new MapResizeRemoved(
                document.Props.Length - props.Length,
                document.Decals.Length - decals.Length,
                removedUnits,
                outside.Length,
                emptied.Count,
                document.Water.WaveGenerators.Length - waves.Length);
            return new MapResizeResult(moved, dx, dz, removed, spawnsMoved, clipped - emptied.Count);
        }
    }
}
