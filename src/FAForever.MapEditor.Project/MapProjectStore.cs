using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// Keeps a <see cref="MapDocument"/> on disk as a project (<see cref="MapProjectLayout"/>): its
    /// records as JSON, its images as PNG, its script and other files as they are.
    /// </summary>
    public static partial class MapProjectStore
    {
        /// <summary>
        /// Writes the document into <paramref name="folder"/>, which must be absent, empty or a
        /// project. Files of an earlier save that the document no longer has are removed from
        /// <see cref="MapProjectLayout.Files"/>.
        /// </summary>
        /// <param name="previous">The document as last saved in this folder: the images whose part
        /// did not change since are not written again. A JSON file is only written when its text
        /// changes.</param>
        /// <exception cref="MapProjectException">The folder holds something other than a project.</exception>
        public static void Save(MapDocument document, string folder, MapDocument? previous = null)
        {
            if (Directory.Exists(folder)
                && Directory.EnumerateFileSystemEntries(folder).Any()
                && !File.Exists(Path.Combine(folder, MapProjectLayout.Map)))
            {
                throw new MapProjectException($"{folder} is not empty and holds no project ({MapProjectLayout.Map})");
            }
            Directory.CreateDirectory(folder);
            string Of(string file) => Path.Combine(folder, file);

            Directory.CreateDirectory(Of(MapProjectLayout.ProceduralSources));
            Directory.CreateDirectory(Of(MapProjectLayout.ProceduralTemp));

            // an image is written when its part changed since the previous save, or is not on disk
            bool Stale(string file, Func<MapDocument, object> part) =>
                previous is null || !ReferenceEquals(part(document), part(previous)) || !File.Exists(Of(file));

            ProjectJson.Write(Of(MapProjectLayout.Map), MapProjectMapping.ToDto(document));
            ProjectJson.Write(Of(MapProjectLayout.Props), document.Props.Select(MapProjectMapping.ToDto).ToList());
            ProjectJson.Write(Of(MapProjectLayout.Decals), MapProjectMapping.ToDecalsDto(document));
            ProjectJson.Write(Of(MapProjectLayout.Markers), MapProjectMapping.ToDto(document.Markers));
            ProjectJson.Write(Of(MapProjectLayout.Armies), document.Armies.Select(MapProjectMapping.ToDto).ToList());

            // a grid a stack of layers makes is the editor's, named so (.gen); without a stack it is the map's own:
            // written under its name, and the file under the other name removed
            void SaveGrid(LayerTarget target, Func<MapDocument, object> part, Action<string> write)
            {
                string file = MapProjectLayout.GridFile(document, target);
                string plain = MapProjectLayout.GridFile(target);
                string other = file == plain ? MapProjectLayout.Generated(plain) : plain;
                if (Stale(file, part))
                {
                    write(Of(file));
                }
                if (File.Exists(Of(other)))
                {
                    File.Delete(Of(other));
                }
            }

            Terrain terrain = document.Terrain;
            SaveGrid(LayerTarget.Heightmap, d => d.Terrain.Heightmap.Samples, file =>
            {
                TiledBuffer<ushort> heights = terrain.Heightmap.Samples;
                ProjectImages.WriteGray16(file, heights.Width, heights.Height, heights.ToArray());
            });
            if (Stale(MapProjectLayout.TerrainTypes, d => d.Terrain.Types))
            {
                WriteGray8(Of(MapProjectLayout.TerrainTypes), terrain.Types);
            }
            if (Stale(MapProjectLayout.NormalMap, d => d.Terrain.NormalMap))
            {
                WriteRgba(Of(MapProjectLayout.NormalMap), terrain.NormalMap);
            }
            if (Stale(MapProjectLayout.Preview, d => d.Terrain.Preview))
            {
                WriteRgba(Of(MapProjectLayout.Preview), terrain.Preview);
            }
            for (int layer = 0; layer < terrain.StratumMasks.Layers.Length; layer++)
            {
                int index = layer;
                SaveGrid(LayerTarget.Stratum(layer + 1), d => d.Terrain.StratumMasks.Layers.Length > index ? d.Terrain.StratumMasks.Layers[index] : d,
                    file => WriteGray8(file, terrain.StratumMasks.Layers[index]));
            }

            Water water = document.Water;
            if (Stale(MapProjectLayout.WaterMap, d => d.Water.Map))
            {
                WriteRgba(Of(MapProjectLayout.WaterMap), water.Map);
            }
            SaveGrid(LayerTarget.Water(WaterMask.Foam), d => d.Water.FoamMask, file => WriteGray8(file, water.FoamMask));
            SaveGrid(LayerTarget.Water(WaterMask.Flatness), d => d.Water.FlatnessMask, file => WriteGray8(file, water.FlatnessMask));
            SaveGrid(LayerTarget.Water(WaterMask.DepthBias), d => d.Water.DepthBiasMask, file => WriteGray8(file, water.DepthBiasMask));

            SaveMasks(document, folder, previous);
            SaveWalkabilityMarks(document, folder, previous);
            SaveRules(document, folder);
            SaveHeightStack(document, folder, previous);
            SaveCoverageStacks(document, folder, previous);

            if (document.Script is ImmutableArray<byte> script)
            {
                WriteBytes(Of(MapProjectLayout.Script), script);
            }
            else
            {
                File.Delete(Of(MapProjectLayout.Script));
            }

            string files = Of(MapProjectLayout.Files);
            foreach ((string path, ImmutableArray<byte> bytes) in document.Files)
            {
                string target = Path.Combine(files, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                WriteBytes(target, bytes);
            }
            foreach ((string path, string file) in FilesIn(files))
            {
                if (!document.Files.ContainsKey(path))
                {
                    File.Delete(file);
                }
            }
        }

        /// <summary>
        /// Reads the project in <paramref name="folder"/>.
        /// </summary>
        /// <exception cref="MapProjectException">A file is missing, does not read, or does not fit the map's size.</exception>
        public static MapDocument Load(string folder)
        {
            string Of(string file) => Path.Combine(folder, file);

            MapProjectDto map = ProjectJson.Read<MapProjectDto>(Of(MapProjectLayout.Map));
            List<PropDto> props = ProjectJson.Read<List<PropDto>>(Of(MapProjectLayout.Props));
            DecalsDto decals = ProjectJson.Read<DecalsDto>(Of(MapProjectLayout.Decals));
            MarkerSetDto markers = ProjectJson.Read<MarkerSetDto>(Of(MapProjectLayout.Markers));
            List<ArmyDto> armies = ProjectJson.Read<List<ArmyDto>>(Of(MapProjectLayout.Armies));

            int width = map.Terrain.Width;
            int height = map.Terrain.Height;
            MapProjectImages images = new MapProjectImages
            {
                Heightmap = TiledBuffer<ushort>.FromArray(width + 1, height + 1, ReadHeightmap(folder, width, height)),
                TerrainTypes = ReadGray8(Of(MapProjectLayout.TerrainTypes), width, height),
                StratumMasks = ReadStratumMasks(folder, map.Terrain.ScmapVersion >= 56 ? 8 : 4, width, height),
                NormalMap = ReadRgba(Of(MapProjectLayout.NormalMap)),
                Preview = Pixels.FromRgba(256, 256, ProjectImages.ReadRgba(Of(MapProjectLayout.Preview), 256, 256)),
                WaterMap = ReadRgba(Of(MapProjectLayout.WaterMap)),
                WaterFoamMask = ReadGray8(Existing(folder, MapProjectLayout.WaterFoam), width / 2, height / 2),
                WaterFlatnessMask = ReadGray8(Existing(folder, MapProjectLayout.WaterFlatness), width / 2, height / 2),
                WaterDepthBiasMask = ReadGray8(Existing(folder, MapProjectLayout.WaterDepthBias), width / 2, height / 2),
                Masks = ReadMasks(folder, width, height),
            };

            string scriptPath = Of(MapProjectLayout.Script);
            ImmutableArray<byte>? script = File.Exists(scriptPath) ? ImmutableCollectionsMarshal.AsImmutableArray(File.ReadAllBytes(scriptPath)) : null;
            ImmutableSortedDictionary<string, ImmutableArray<byte>> files = FilesIn(Of(MapProjectLayout.Files))
                .ToImmutableSortedDictionary(entry => entry.Path, entry => ImmutableCollectionsMarshal.AsImmutableArray(File.ReadAllBytes(entry.File)), StringComparer.Ordinal);

            try
            {
                MapDocument document = MapProjectMapping.ToDocument(map, props, decals, markers, armies, images, script, files);
                return LoadHeightStack(LoadCoverageStacks(WithRules(WithWalkabilityMarks(document, folder), folder), folder), folder);
            }
            catch (ArgumentException exception)
            {
                throw new MapProjectException($"{folder}: {exception.Message}", exception);
            }
        }

        /// <summary>
        /// The document with the parts that the changed files of the project hold read again, e.g.
        /// after a map maker saved <c>heightmap.png</c> from another tool. A file whose content is
        /// what the document already holds changes nothing, so the editor's own saves are no edits.
        /// </summary>
        /// <param name="changedFiles">The files that changed (or were removed), relative to the
        /// project folder, with <c>/</c> or <c>\</c> between folders.</param>
        /// <returns><paramref name="current"/> itself when nothing changed.</returns>
        /// <exception cref="MapProjectException">A file does not read, or changes the map's size.</exception>
        public static MapDocument Reload(MapDocument current, string folder, IEnumerable<string> changedFiles)
        {
            string Of(string file) => Path.Combine(folder, file);
            MapDocument document = current;
            int width = current.Width;
            int height = current.Height;

            foreach (string changed in changedFiles.Select(file => file.Replace('\\', '/')).Distinct())
            {
                if (MapProjectLayout.IsGenerated(changed))
                {
                    // the editor's own (.gen): made from other things, never read back
                    continue;
                }
                switch (changed)
                {
                    case MapProjectLayout.Map:
                        document = ReloadJson(document, Of(changed), MapProjectMapping.ToDto(document), (d, map) => MapProjectMapping.WithMap(d, map));
                        break;
                    case MapProjectLayout.Props:
                        document = ReloadJson(document, Of(changed), document.Props.Select(MapProjectMapping.ToDto).ToList(), MapProjectMapping.WithProps);
                        break;
                    case MapProjectLayout.Decals:
                        document = ReloadJson(document, Of(changed), MapProjectMapping.ToDecalsDto(document), MapProjectMapping.WithDecals);
                        break;
                    case MapProjectLayout.Markers:
                        document = ReloadJson(document, Of(changed), MapProjectMapping.ToDto(document.Markers), (d, markers) => d with { Markers = MapProjectMapping.ToModel(markers) });
                        break;
                    case MapProjectLayout.Armies:
                        document = ReloadJson(document, Of(changed), document.Armies.Select(MapProjectMapping.ToDto).ToList(), (d, armies) => d with { Armies = [.. armies.Select(MapProjectMapping.ToModel)] });
                        break;
                    case MapProjectLayout.Heightmap or MapProjectLayout.HeightmapRaw:
                    {
                        TiledBuffer<ushort> samples = document.Terrain.Heightmap.Samples;
                        ushort[] heights = ReadHeightmap(folder, width, height);
                        if (!document.HeightStack.IsEmpty)
                        {
                            // with height layers the heightmap is their sum: another tool's heightmap becomes the base
                            if (!heights.AsSpan().SequenceEqual(samples.ToArray()))
                            {
                                document = HeightLayerEdits.SetBase(document, TiledBuffer<ushort>.FromArray(width + 1, height + 1, heights));
                            }
                        }
                        else if (!heights.AsSpan().SequenceEqual(samples.ToArray()))
                        {
                            document = document with { Terrain = document.Terrain with { Heightmap = document.Terrain.Heightmap with { Samples = TiledBuffer<ushort>.FromArray(width + 1, height + 1, heights) } } };
                        }
                        break;
                    }
                    case MapProjectLayout.TerrainTypes:
                        document = ReloadGray8(document, Of(changed), document.Terrain.Types, width, height, (d, types) => d with { Terrain = d.Terrain with { Types = types } });
                        break;
                    case MapProjectLayout.NormalMap:
                        document = ReloadRgba(document, Of(changed), document.Terrain.NormalMap, (d, image) => d with { Terrain = d.Terrain with { NormalMap = image } });
                        break;
                    case MapProjectLayout.Preview:
                        document = ReloadRgba(document, Of(changed), document.Terrain.Preview, (d, image) => d with { Terrain = d.Terrain with { Preview = image } });
                        break;
                    case MapProjectLayout.WaterMap:
                        document = ReloadRgba(document, Of(changed), document.Water.Map, (d, image) => d with { Water = d.Water with { Map = image } });
                        break;
                    case MapProjectLayout.WaterFoam:
                        document = ReloadGray8(document, Of(changed), document.Water.FoamMask, width / 2, height / 2, (d, mask) => d with { Water = d.Water with { FoamMask = mask } });
                        break;
                    case MapProjectLayout.WaterFlatness:
                        document = ReloadGray8(document, Of(changed), document.Water.FlatnessMask, width / 2, height / 2, (d, mask) => d with { Water = d.Water with { FlatnessMask = mask } });
                        break;
                    case MapProjectLayout.WaterDepthBias:
                        document = ReloadGray8(document, Of(changed), document.Water.DepthBiasMask, width / 2, height / 2, (d, mask) => d with { Water = d.Water with { DepthBiasMask = mask } });
                        break;
                    case MapProjectLayout.PropRules:
                        document = ReloadPropRules(document, Of(changed));
                        break;
                    case MapProjectLayout.WalkabilityMarks:
                        document = ReloadWalkabilityMarks(document, folder);
                        break;
                    case MapProjectLayout.Script:
                    {
                        ImmutableArray<byte>? script = File.Exists(Of(changed)) ? ImmutableCollectionsMarshal.AsImmutableArray(File.ReadAllBytes(Of(changed))) : null;
                        if (!SameBytes(script, document.Script))
                        {
                            document = document with { Script = script };
                        }
                        break;
                    }
                    default:
                        if (changed.StartsWith(MapProjectLayout.StratumMasks + "/", StringComparison.Ordinal)
                            && int.TryParse(Path.GetFileNameWithoutExtension(changed).Replace("stratum-", ""), out int layer)
                            && changed == MapProjectLayout.StratumMask(layer)
                            && layer >= 1 && layer <= document.Terrain.StratumMasks.Layers.Length)
                        {
                            StratumMasks masks = document.Terrain.StratumMasks;
                            if (!File.Exists(Of(changed)) && File.Exists(Of(MapProjectLayout.Generated(changed))))
                            {
                                // removed because a stack makes the mask now: the editor's (.gen) holds it
                                break;
                            }
                            TiledBuffer<byte> mask = File.Exists(Of(changed))
                                ? ReadGray8(Of(changed), masks.Width, masks.Height)
                                : TiledBuffer<byte>.Create(masks.Width, masks.Height);
                            if (!mask.ToArray().AsSpan().SequenceEqual(masks.Layers[layer - 1].ToArray()))
                            {
                                document = document with { Terrain = document.Terrain with { StratumMasks = masks with { Layers = masks.Layers.SetItem(layer - 1, mask) } } };
                            }
                        }
                        else if (MapProjectLayout.IsMaskRules(changed, out string? ruled))
                        {
                            document = ReloadMaskRules(document, Of(changed), ruled);
                        }
                        else if (changed.StartsWith(MapProjectLayout.Masks + "/", StringComparison.Ordinal)
                            && changed.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                            && changed.IndexOf('/', MapProjectLayout.Masks.Length + 1) < 0)
                        {
                            document = ReloadMask(document, Of(changed), Path.GetFileNameWithoutExtension(changed));
                        }
                        else if (IsCoverageStackFile(changed, out string stackKey))
                        {
                            document = ReloadCoverageStack(document, folder, stackKey);
                        }
                        else if (IsHeightStackFile(folder, changed))
                        {
                            document = ReloadHeightStack(document, folder);
                        }
                        else if (IsImageLayerFile(document, folder, changed))
                        {
                            // a tool's file that image layers read (of the heightmap, of masks): each reads it again
                            if (ImportedLayerOf(document, folder, changed) is Layer imported)
                            {
                                document = ReloadImported(document, folder, imported);
                            }
                            foreach ((LayerTarget target, Layer image) in CoverageImageLayersOf(document, folder, changed))
                            {
                                document = ReloadCoverageImage(document, folder, target, image);
                            }
                        }
                        else if (changed.StartsWith(MapProjectLayout.Files + "/", StringComparison.Ordinal))
                        {
                            string path = changed[(MapProjectLayout.Files.Length + 1)..];
                            string file = Of(changed);
                            ImmutableArray<byte>? bytes = File.Exists(file) ? ImmutableCollectionsMarshal.AsImmutableArray(File.ReadAllBytes(file)) : null;
                            if (!SameBytes(bytes, document.Files.TryGetValue(path, out ImmutableArray<byte> held) ? held : null))
                            {
                                document = document with { Files = bytes is ImmutableArray<byte> value ? document.Files.SetItem(path, value) : document.Files.Remove(path) };
                            }
                        }
                        break;
                }
            }
            return document;
        }

        /// <summary>
        /// Writes the masks for tools whose pixels changed since the previous save, and removes the
        /// files of masks the document no longer has. The folder is made only for a first mask.
        /// </summary>
        private static void SaveMasks(MapDocument document, string folder, MapDocument? previous)
        {
            string masks = Path.Combine(folder, MapProjectLayout.Masks);
            HashSet<string> wanted = new HashSet<string>(document.Masks.Keys.Select(name => Path.GetFileName(MapProjectLayout.GridFile(document, LayerTarget.Mask(name)))), StringComparer.Ordinal);
            if (Directory.Exists(masks))
            {
                foreach (string file in Directory.EnumerateFiles(masks, "*.png").ToList())
                {
                    // gone, renamed to another case of its name (Windows compares file names without case), or
                    // made by a stack since (.gen) or no longer: written again below
                    if (!wanted.Contains(Path.GetFileName(file)))
                    {
                        File.Delete(file);
                    }
                }
            }
            foreach ((string name, TiledBuffer<byte> mask) in document.Masks)
            {
                string file = Path.Combine(folder, MapProjectLayout.GridFile(document, LayerTarget.Mask(name)));
                bool unchanged = previous is not null && previous.Masks.TryGetValue(name, out TiledBuffer<byte>? before) && ReferenceEquals(before, mask);
                if (!unchanged || !File.Exists(file))
                {
                    WriteGray8(file, mask);
                }
            }
        }

        /// <summary>
        /// The masks for tools of a project: every PNG in its <see cref="MapProjectLayout.Masks"/>
        /// folder, the map's size or half of it, named after the file (<c>forest.png</c>, or
        /// <c>forest.gen.png</c> while a stack makes it; the map's own name first).
        /// </summary>
        /// <exception cref="MapProjectException">A mask has another size.</exception>
        private static ImmutableSortedDictionary<string, TiledBuffer<byte>> ReadMasks(string folder, int width, int height)
        {
            string masks = Path.Combine(folder, MapProjectLayout.Masks);
            if (!Directory.Exists(masks))
            {
                return ImmutableSortedDictionary.Create<string, TiledBuffer<byte>>(StringComparer.Ordinal);
            }
            return Directory.EnumerateFiles(masks, "*.png")
                .GroupBy(MaskNameOf, StringComparer.Ordinal)
                .ToImmutableSortedDictionary(group => group.Key, group => ReadMask(group.OrderBy(MapProjectLayout.IsGenerated).First(), width, height), StringComparer.Ordinal);
        }

        /// <summary>
        /// The name of the mask for tools a file of the masks folder holds: <c>forest.gen.png</c> holds <c>forest</c>.
        /// </summary>
        private static string MaskNameOf(string file) => Path.GetFileNameWithoutExtension(MapProjectLayout.WithoutGenerated(file));

        private static TiledBuffer<byte> ReadMask(string file, int width, int height)
        {
            (int maskWidth, int maskHeight) = ProjectImages.Size(file);
            if (!MaskLayers.Fits(width, height, maskWidth, maskHeight))
            {
                throw new MapProjectException($"{file} is {maskWidth} by {maskHeight} pixels: a mask is the map's size ({width} by {height}) or half of it ({Math.Max(1, width / 2)} by {Math.Max(1, height / 2)})");
            }
            return ReadGray8(file, maskWidth, maskHeight);
        }

        /// <summary>
        /// The document with a mask for tools read again, added, or (its file removed) taken away.
        /// </summary>
        private static MapDocument ReloadMask(MapDocument document, string file, string name)
        {
            // the map's own file removed because a stack makes the mask now: the editor's (.gen) holds it
            if (!File.Exists(file) && File.Exists(MapProjectLayout.Generated(file)))
            {
                return document;
            }
            if (!File.Exists(file))
            {
                return document.Masks.ContainsKey(name) ? document with { Masks = document.Masks.Remove(name) } : document;
            }
            TiledBuffer<byte> read = ReadMask(file, document.Width, document.Height);
            if (document.Masks.TryGetValue(name, out TiledBuffer<byte>? held)
                && held.Width == read.Width && held.Height == read.Height
                && read.ToArray().AsSpan().SequenceEqual(held.ToArray()))
            {
                return document;
            }
            return document with { Masks = document.Masks.SetItem(name, read) };
        }

        /// <summary>
        /// Reads a JSON file again when its text is not what the document would write.
        /// </summary>
        private static MapDocument ReloadJson<T>(MapDocument document, string path, T held, Func<MapDocument, T, MapDocument> apply)
        {
            if (!File.Exists(path) || File.ReadAllText(path).ReplaceLineEndings("\n") == ProjectJson.Serialize(held).ReplaceLineEndings("\n"))
            {
                return document;
            }
            return apply(document, ProjectJson.Read<T>(path));
        }

        private static MapDocument ReloadGray8(MapDocument document, string path, TiledBuffer<byte> held, int width, int height, Func<MapDocument, TiledBuffer<byte>, MapDocument> apply)
        {
            if (!File.Exists(path))
            {
                return document;
            }
            TiledBuffer<byte> read = ReadGray8(path, width, height);
            return read.ToArray().AsSpan().SequenceEqual(held.ToArray()) ? document : apply(document, read);
        }

        private static MapDocument ReloadRgba(MapDocument document, string path, TiledBuffer<Color32> held, Func<MapDocument, TiledBuffer<Color32>, MapDocument> apply)
        {
            if (!File.Exists(path))
            {
                return document;
            }
            TiledBuffer<Color32> read = ReadRgba(path);
            return read.Width == held.Width && read.Height == held.Height && read.ToArray().AsSpan().SequenceEqual(held.ToArray())
                ? document
                : apply(document, read);
        }

        private static bool SameBytes(ImmutableArray<byte>? a, ImmutableArray<byte>? b) =>
            a is null ? b is null : b is not null && a.Value.AsSpan().SequenceEqual(b.Value.AsSpan());

        /// <summary>
        /// Writes a file unless it already holds these bytes, so that tools watching it see no change.
        /// </summary>
        private static void WriteBytes(string path, ImmutableArray<byte> bytes)
        {
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes.AsSpan()))
            {
                File.WriteAllBytes(path, ImmutableCollectionsMarshal.AsArray(bytes)!);
            }
        }

        /// <summary>
        /// The full path of a grid's file as the project has it: the map's own name, else the name of
        /// the editor's (<see cref="MapProjectLayout.Generated"/>, while a stack of layers makes the
        /// grid), else the map's own name (missing).
        /// </summary>
        private static string Existing(string folder, string file)
        {
            string plain = Path.Combine(folder, file);
            string generated = Path.Combine(folder, MapProjectLayout.Generated(file));
            return File.Exists(plain) || !File.Exists(generated) ? plain : generated;
        }

        private static ushort[] ReadHeightmap(string folder, int width, int height)
        {
            string png = Path.Combine(folder, MapProjectLayout.Heightmap);
            if (File.Exists(png))
            {
                return ProjectImages.ReadGray16(png, width + 1, height + 1);
            }

            string raw = Path.Combine(folder, MapProjectLayout.HeightmapRaw);
            string generated = Path.Combine(folder, MapProjectLayout.Generated(MapProjectLayout.Heightmap));
            if (!File.Exists(raw) && File.Exists(generated))
            {
                // what the height layers made at the last save; opening makes it again from them
                return ProjectImages.ReadGray16(generated, width + 1, height + 1);
            }
            if (!File.Exists(raw))
            {
                throw new MapProjectException($"{folder} has no {MapProjectLayout.Heightmap}, {MapProjectLayout.HeightmapRaw} or {MapProjectLayout.Generated(MapProjectLayout.Heightmap)}");
            }
            byte[] bytes = File.ReadAllBytes(raw);
            int expected = (width + 1) * (height + 1) * 2;
            if (bytes.Length != expected)
            {
                throw new MapProjectException($"{raw} has {bytes.Length} bytes, expected {expected}: {width + 1} by {height + 1} samples of 16 bits");
            }
            ushort[] samples = new ushort[(width + 1) * (height + 1)];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (ushort)(bytes[i * 2] | bytes[i * 2 + 1] << 8);
            }
            return samples;
        }

        /// <summary>
        /// The masks of the layers. A missing mask leaves its layer unused; the masks that exist must
        /// all have the same size (the map's size when there are none).
        /// </summary>
        private static StratumMasks ReadStratumMasks(string folder, int count, int mapWidth, int mapHeight)
        {
            string[] paths = [.. Enumerable.Range(1, count).Select(layer => Existing(folder, MapProjectLayout.StratumMask(layer)))];
            (int width, int height) = paths.FirstOrDefault(File.Exists) is string first ? ProjectImages.Size(first) : (mapWidth, mapHeight);
            return new StratumMasks
            {
                Layers = [.. paths.Select(path => File.Exists(path) ? ReadGray8(path, width, height) : TiledBuffer<byte>.Create(width, height))],
            };
        }

        private static TiledBuffer<byte> ReadGray8(string path, int width, int height) =>
            TiledBuffer<byte>.FromArray(width, height, ProjectImages.ReadGray8(path, width, height));

        /// <summary>
        /// An image of any size.
        /// </summary>
        private static TiledBuffer<Color32> ReadRgba(string path)
        {
            if (!File.Exists(path))
            {
                throw new MapProjectException($"{path} is missing");
            }
            (int width, int height) = ProjectImages.Size(path);
            return Pixels.FromRgba(width, height, ProjectImages.ReadRgba(path, width, height));
        }

        private static void WriteGray8(string path, TiledBuffer<byte> buffer) =>
            ProjectImages.WriteGray8(path, buffer.Width, buffer.Height, buffer.ToArray());

        private static void WriteRgba(string path, TiledBuffer<Color32> image) =>
            ProjectImages.WriteRgba(path, image.Width, image.Height, Pixels.ToRgba(image));

        /// <summary>
        /// The files under a folder by their path in it, with <c>/</c> between folders.
        /// </summary>
        private static IEnumerable<(string Path, string File)> FilesIn(string folder) =>
            Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(file => (Path.GetRelativePath(folder, file).Replace('\\', '/'), file)).ToList()
                : [];
    }
}
