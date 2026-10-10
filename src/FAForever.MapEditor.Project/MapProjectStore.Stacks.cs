using System.Collections.Immutable;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// The stacks of layers of a project (format 6, user story T-5): per target
    /// <c>stacks/&lt;target&gt;.json</c> with a PNG per layer in <c>stacks/&lt;target&gt;/</c>
    /// (<see cref="MapProjectLayout.StackIndex"/>). Projects of format 3 to 5 keep the heightmap's
    /// layers in <c>layers/</c> (<see cref="MapProjectLayout.HeightLayersIndex"/>): they are read into
    /// the heightmap's stack, and the first save writes them as a stack and removes the old files.
    /// </summary>
    public static partial class MapProjectStore
    {
        /// <summary>
        /// The height in game units of a sample 0 in a layer's PNG: layers of differences hold them
        /// around 0, so 0 lies in the middle of the 16 bits.
        /// </summary>
        private static float OffsetOf(Layer layer, float scale) =>
            HeightLayerEdits.IsDifference(layer.Combine) ? -32768 * scale : 0;

        /// <summary>
        /// Whether a layer of a source keeps its grid in a file: a curve's and a mask reference's
        /// values follow from the source.
        /// </summary>
        private static bool HasFile(Layer layer) => layer.Source is not (CurveSource or MaskRefSource);

        /// <summary>
        /// Writes the heightmap's stack and removes the files of a project of format 5 or older.
        /// </summary>
        private static void SaveHeightStack(MapDocument document, string folder, MapDocument? previous)
        {
            SaveStack(document, document.HeightStack, folder, previous?.HeightStack, previous?.Terrain.Heightmap.Scale);
            RemoveLegacyHeightLayers(folder);
        }

        /// <summary>
        /// Writes a stack: the layers whose grids changed since the previous save, the index, and
        /// removes the files of layers gone; a stack without layers has no index and no folder.
        /// </summary>
        /// <param name="previousScale">The height scale of the previous save; a new scale writes every height layer again.</param>
        private static void SaveStack(MapDocument document, LayerStack stack, string folder, LayerStack? previous, float? previousScale)
        {
            string key = stack.Target.Key;
            string directory = Path.Combine(folder, MapProjectLayout.Stacks, key);
            string index = Path.Combine(folder, MapProjectLayout.StackIndex(key));
            bool heights = stack.Target.IsHeights;
            float scale = document.Terrain.Heightmap.Scale;
            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Layer layer in stack.Layers.Where(HasFile))
            {
                files.Add(Path.GetFileName(MapProjectLayout.StackLayer(key, layer.Name)));
                if (heights && layer.Coverage is not null)
                {
                    files.Add(Path.GetFileName(MapProjectLayout.StackLayerCoverage(key, layer.Name)));
                }
            }
            if (Directory.Exists(directory))
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*.png").ToList())
                {
                    if (!files.Contains(Path.GetFileName(file)))
                    {
                        File.Delete(file);
                    }
                }
                if (stack.IsEmpty && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            if (stack.IsEmpty)
            {
                if (File.Exists(index))
                {
                    File.Delete(index);
                }
                string stacks = Path.Combine(folder, MapProjectLayout.Stacks);
                if (Directory.Exists(stacks) && !Directory.EnumerateFileSystemEntries(stacks).Any())
                {
                    Directory.Delete(stacks);
                }
                return;
            }

            Directory.CreateDirectory(directory);
            foreach (Layer layer in stack.Layers.Where(HasFile))
            {
                Layer? before = previous?.Find(layer.Name);
                string values = Path.Combine(folder, MapProjectLayout.StackLayer(key, layer.Name));
                if (heights)
                {
                    bool sameEncoding = before is not null && before.Name == layer.Name && OffsetOf(before, scale) == OffsetOf(layer, scale) && previousScale == scale;
                    if (layer.Values is TiledBuffer<float> heightValues && (!sameEncoding || !ReferenceEquals(before!.Values, heightValues) || !File.Exists(values)))
                    {
                        float offset = OffsetOf(layer, scale);
                        float[] array = heightValues.ToArray();
                        ushort[] samples = new ushort[array.Length];
                        for (int i = 0; i < array.Length; i++)
                        {
                            samples[i] = (ushort)Math.Clamp(MathF.Round((array[i] - offset) / scale), 0, ushort.MaxValue);
                        }
                        ProjectImages.WriteGray16(values, heightValues.Width, heightValues.Height, samples);
                    }
                    if (layer.Coverage is TiledBuffer<byte> coverage)
                    {
                        string file = Path.Combine(folder, MapProjectLayout.StackLayerCoverage(key, layer.Name));
                        if (before is null || before.Name != layer.Name || !ReferenceEquals(before.Coverage, coverage) || !File.Exists(file))
                        {
                            WriteGray8(file, coverage);
                        }
                    }
                }
                else if (layer.Coverage is TiledBuffer<byte> coverage
                    && (before is null || before.Name != layer.Name || !ReferenceEquals(before.Coverage, coverage) || !File.Exists(values)))
                {
                    WriteGray8(values, coverage);
                }
            }
            ProjectJson.Write(index, ToDto(stack, scale));
        }

        private static LayerStackDto ToDto(LayerStack stack, float scale) => new LayerStackDto
        {
            Target = stack.Target.Key,
            Layers = [.. stack.Layers.Select(layer =>
            {
                MaskRefSource? reference = layer.Source as MaskRefSource;
                return new LayerDto
                {
                    Name = layer.Name,
                    Source = LayerSources.NameOf(layer.Source),
                    Combine = layer.Combine,
                    Strength = layer.Strength == 1 ? null : layer.Strength,
                    Clip = layer.Clip,
                    Hidden = layer.Visible ? null : true,
                    Locked = layer.Locked ? true : null,
                    Base = layer.IsBase ? true : null,
                    Offset = stack.Target.IsHeights && HasFile(layer) ? OffsetOf(layer, scale) : null,
                    Path = layer.Path,
                    Mask = reference?.Key,
                    Threshold = reference is { Threshold: not 0 } ? reference.Threshold : null,
                    Invert = reference is { Invert: true } ? true : null,
                    Height = reference is { Height: not 0 } ? reference.Height : null,
                    Rules = layer.Source is RulesSource rules ? MapProjectMapping.ToDto(rules.Rules) : null,
                    Curve = layer.Curve is HeightCurve curve ? ToDto(curve) : null,
                };
            })],
        };

        private static HeightCurveDto ToDto(HeightCurve curve) => new HeightCurveDto
        {
            Profile = curve.Profile,
            Width = curve.Width,
            Falloff = curve.Falloff,
            Closed = curve.Closed ? true : null,
            Symmetry = MapProjectMapping.ToDto(curve.Symmetry),
            Points = [.. curve.Points.Select(point => new[] { point.X, point.Z, point.Height })],
        };

        private static HeightCurve ToModel(HeightCurveDto c) => new HeightCurve
        {
            Profile = c.Profile,
            Width = c.Width,
            Falloff = c.Falloff,
            Closed = c.Closed == true,
            Symmetry = MapProjectMapping.ToModel(c.Symmetry),
            Points = [.. c.Points.Where(point => point.Length >= 2).Select(point => new CurvePoint(point[0], point[1], point.Length > 2 ? point[2] : 0))],
        };

        /// <summary>
        /// The document with the project's heightmap stack (or the layers of an older project), and
        /// its heightmap what they make.
        /// </summary>
        /// <exception cref="MapProjectException">A layer's file is missing or does not fit.</exception>
        private static MapDocument LoadHeightStack(MapDocument document, string folder)
        {
            LayerStack stack = ReadHeightStack(document, folder);
            return stack.IsEmpty ? document : HeightLayers.Recomposite(document with { HeightStack = stack });
        }

        /// <summary>
        /// The heightmap's stack as the project's files hold it: <c>stacks/heightmap.json</c>, else
        /// the <c>layers/</c> of a project of format 5 or older, else none.
        /// </summary>
        private static LayerStack ReadHeightStack(MapDocument document, string folder)
        {
            LayerStack stack = File.Exists(Path.Combine(folder, MapProjectLayout.HeightStackIndex))
                ? ReadStack(document, LayerTarget.Heightmap, folder)
                : ReadLegacyHeightLayers(document, folder);
            if (!stack.IsEmpty && (!stack.Layers[0].IsBase || stack.Layers.Skip(1).Any(layer => layer.IsBase)))
            {
                throw new MapProjectException($"{Path.Combine(folder, MapProjectLayout.HeightStackIndex)}: the lowest height layer is the base, and there is one base");
            }
            return stack;
        }

        /// <summary>
        /// A target's stack as the project's files hold it (an empty one without its index); the
        /// grids of a layer the document holds already share its tiles where they are the same, so a
        /// read that changes nothing gives the same layer.
        /// </summary>
        /// <exception cref="MapProjectException">A layer's file is missing or does not fit, or its source is unknown.</exception>
        private static LayerStack ReadStack(MapDocument document, LayerTarget target, string folder)
        {
            string index = Path.Combine(folder, MapProjectLayout.StackIndex(target.Key));
            LayerStack held = target.IsHeights ? document.HeightStack : LayerStack.Empty(target);
            if (!File.Exists(index))
            {
                return LayerStack.Empty(target);
            }
            LayerStackDto dto = ProjectJson.Read<LayerStackDto>(index);
            StackGrid grid = LayerStacks.GridOf(document, held);
            float scale = document.Terrain.Heightmap.Scale;
            ImmutableArray<Layer>.Builder layers = ImmutableArray.CreateBuilder<Layer>();
            foreach (LayerDto entry in dto.Layers)
            {
                Layer? before = held.Find(entry.Name) is Layer found && found.Name == entry.Name ? found : null;
                LayerSource source = entry.Source switch
                {
                    LayerSources.PaintedName => LayerSource.Painted,
                    LayerSources.ImageName => new ImageSource(entry.Path ?? throw new MapProjectException($"{index}: the image layer {entry.Name} names no file")),
                    LayerSources.MaskName => new MaskRefSource(entry.Mask ?? throw new MapProjectException($"{index}: the mask layer {entry.Name} names no mask"),
                        entry.Threshold ?? 0, entry.Invert == true, entry.Height ?? 0),
                    LayerSources.RulesName => new RulesSource(entry.Rules is MaskRulesDto rules ? MapProjectMapping.ToModel(rules) : new MaskRuleSet()),
                    LayerSources.CurveName => new CurveSource(entry.Curve is HeightCurveDto curve ? ToModel(curve) : new HeightCurve()),
                    _ => throw new MapProjectException($"{index}: the layer {entry.Name} has a source '{entry.Source}' this editor does not know"),
                };
                Layer layer = new Layer
                {
                    Name = entry.Name,
                    Source = source,
                    Combine = entry.Combine,
                    Strength = Math.Clamp(entry.Strength ?? 1, 0, 1),
                    Clip = entry.Clip,
                    Visible = entry.Hidden != true,
                    Locked = entry.Locked == true,
                    IsBase = entry.Base == true,
                };
                layer = ReadGrids(document, target, folder, grid, scale, layer, before, entry.Offset ?? 0,
                    MapProjectLayout.StackLayer(target.Key, entry.Name), MapProjectLayout.StackLayerCoverage(target.Key, entry.Name));
                layers.Add(before is not null && SameLayer(before, layer) ? before : layer);
            }
            return new LayerStack { Target = target, Layers = layers.ToImmutable() };
        }

        /// <summary>
        /// A layer with its grids read from its files, or made from its source.
        /// </summary>
        private static Layer ReadGrids(MapDocument document, LayerTarget target, string folder, StackGrid grid, float scale, Layer layer, Layer? held,
            float offset, string valuesFile, string coverageFile)
        {
            if (layer.Source is CurveSource { Curve: HeightCurve curve })
            {
                if (held?.Curve is HeightCurve same && same.Equals(curve))
                {
                    return layer with { Values = held.Values, Coverage = held.Coverage };
                }
                (TiledBuffer<float> values, TiledBuffer<byte>? coverage) = HeightCurves.Rasterize(curve, document.Width, document.Height, held?.Values, held?.Coverage);
                return layer with { Values = values, Coverage = coverage };
            }
            if (layer.Source is MaskRefSource)
            {
                return layer;
            }
            if (target.IsHeights)
            {
                // an image layer is read from its file when it is there (another tool may have changed it
                // while the editor was closed); the layer's own PNG holds what it read last
                string? image = layer.Path is string path ? Path.GetFullPath(Path.Combine(folder, path)) : null;
                bool fromImage = image is not null && File.Exists(image);
                ushort[] samples = fromImage
                    ? HeightmapFiles.ReadResampled(document, image!, out _)
                    : ProjectImages.ReadGray16(Path.Combine(folder, valuesFile), grid.Width, grid.Height);
                offset = fromImage ? 0 : offset;
                TiledBuffer<float> values = HeightLayerEdits.FromSamples(TiledBuffer<ushort>.FromArray(grid.Width, grid.Height, samples), scale, held?.Values, offset);
                TiledBuffer<byte>? coverage = null;
                string file = Path.Combine(folder, coverageFile);
                if (File.Exists(file))
                {
                    coverage = SameOrRead(ReadGray8(file, grid.Width, grid.Height), held?.Coverage);
                }
                return layer with { Values = values, Coverage = coverage };
            }
            return layer with { Coverage = SameOrRead(ReadGray8(Path.Combine(folder, valuesFile), grid.Width, grid.Height), held?.Coverage) };
        }

        private static TiledBuffer<byte> SameOrRead(TiledBuffer<byte> read, TiledBuffer<byte>? held) =>
            held is not null && held.Width == read.Width && held.Height == read.Height && read.ToArray().AsSpan().SequenceEqual(held.ToArray()) ? held : read;

        /// <summary>
        /// The heightmap's layers of a project of format 3 to 5 (<c>layers/layers.json</c>) as a stack.
        /// </summary>
        private static LayerStack ReadLegacyHeightLayers(MapDocument document, string folder)
        {
            string index = Path.Combine(folder, MapProjectLayout.HeightLayersIndex);
            if (!File.Exists(index))
            {
                return LayerStack.Empty(LayerTarget.Heightmap);
            }
            HeightLayersDto dto = ProjectJson.Read<HeightLayersDto>(index);
            StackGrid grid = LayerStacks.GridOf(document, document.HeightStack);
            float scale = document.Terrain.Heightmap.Scale;
            ImmutableArray<Layer>.Builder layers = ImmutableArray.CreateBuilder<Layer>();
            foreach (HeightLayerDto entry in dto.Layers)
            {
                Layer? before = document.HeightStack.Find(entry.Name) is Layer found && found.Name == entry.Name ? found : null;
                LayerSource source = entry.Kind.ToLowerInvariant() switch
                {
                    "imported" => new ImageSource(entry.Source ?? ""),
                    "curve" => new CurveSource(entry.Curve is HeightCurveDto curve ? ToModel(curve) : new HeightCurve()),
                    "base" or "sculpt" => LayerSource.Painted,
                    _ => throw new MapProjectException($"{index}: the height layer {entry.Name} is of a kind '{entry.Kind}' this editor does not know"),
                };
                Layer layer = new Layer
                {
                    Name = entry.Name,
                    Source = source,
                    IsBase = entry.Kind.Equals("base", StringComparison.OrdinalIgnoreCase),
                    Combine = entry.Combine,
                    Strength = Math.Clamp(entry.Strength ?? 1, 0, 1),
                    Clip = entry.Mask,
                    Visible = entry.Hidden != true,
                    Locked = entry.Locked == true,
                };
                layer = ReadGrids(document, LayerTarget.Heightmap, folder, grid, scale, layer, before, entry.Offset ?? 0,
                    MapProjectLayout.HeightLayer(entry.Name), MapProjectLayout.HeightLayerCoverage(entry.Name));
                layers.Add(before is not null && SameLayer(before, layer) ? before : layer);
            }
            return new LayerStack { Target = LayerTarget.Heightmap, Layers = layers.ToImmutable() };
        }

        /// <summary>
        /// Removes <c>layers/layers.json</c> and the layers' files of a project of format 5 or older
        /// once its layers are written as a stack.
        /// </summary>
        private static void RemoveLegacyHeightLayers(string folder)
        {
            string index = Path.Combine(folder, MapProjectLayout.HeightLayersIndex);
            if (!File.Exists(index))
            {
                return;
            }
            HeightLayersDto dto = ProjectJson.Read<HeightLayersDto>(index);
            foreach (HeightLayerDto entry in dto.Layers)
            {
                foreach (string file in new[] { MapProjectLayout.HeightLayer(entry.Name), MapProjectLayout.HeightLayerCoverage(entry.Name) })
                {
                    string path = Path.Combine(folder, file);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }
            File.Delete(index);
            string directory = Path.Combine(folder, MapProjectLayout.HeightLayers);
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }

        private static bool SameLayer(Layer a, Layer b) =>
            a.Name == b.Name && a.Source.Equals(b.Source) && a.Combine == b.Combine && a.Strength == b.Strength && a.Clip == b.Clip
            && a.Visible == b.Visible && a.Locked == b.Locked && a.IsBase == b.IsBase
            && ReferenceEquals(a.Values, b.Values) && ReferenceEquals(a.Coverage, b.Coverage);

        /// <summary>
        /// The document with the heightmap's stack read again after a file of it changed; the
        /// heightmap follows when the edit is kept.
        /// </summary>
        private static MapDocument ReloadHeightStack(MapDocument document, string folder)
        {
            LayerStack stack = ReadHeightStack(document, folder);
            if (stack.IsEmpty)
            {
                // the index was removed: the layers end, the heightmap stays
                return document.HeightStack.IsEmpty ? document : document with { HeightStack = LayerStack.Empty(LayerTarget.Heightmap) };
            }
            return stack.Equals(document.HeightStack) ? document : document with { HeightStack = stack };
        }

        /// <summary>
        /// Whether a changed file of the project belongs to the heightmap's stack: its index or a
        /// layer's PNG, or the <c>layers/</c> of an older project that has no stack yet.
        /// </summary>
        private static bool IsHeightStackFile(string folder, string changed) =>
            changed == MapProjectLayout.HeightStackIndex
            || changed.StartsWith(MapProjectLayout.Stacks + "/" + LayerTarget.Heightmap.Key + "/", StringComparison.Ordinal)
            || (changed.StartsWith(MapProjectLayout.HeightLayers + "/", StringComparison.Ordinal)
                && !File.Exists(Path.Combine(folder, MapProjectLayout.HeightStackIndex)));

        /// <summary>
        /// The image layer whose file is the changed file, if any.
        /// </summary>
        private static Layer? ImportedLayerOf(MapDocument document, string folder, string changed)
        {
            string full = Path.GetFullPath(Path.Combine(folder, changed));
            return document.HeightStack.Layers.FirstOrDefault(layer => layer.Path is string source
                && string.Equals(Path.GetFullPath(Path.Combine(folder, source)), full, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The document with an image layer read again from its file; a file that is gone leaves the
        /// layer as it is.
        /// </summary>
        private static MapDocument ReloadImported(MapDocument document, string folder, Layer layer)
        {
            string path = Path.GetFullPath(Path.Combine(folder, layer.Path!));
            if (!File.Exists(path))
            {
                return document;
            }
            ushort[] samples = HeightmapFiles.ReadResampled(document, path, out _);
            MapDocument read = HeightLayerEdits.SetImported(document, layer.Name, samples);
            return ReferenceEquals(HeightLayers.Find(read, layer.Name)!.Values, layer.Values) ? document : read;
        }
    }
}
