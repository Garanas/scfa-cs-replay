using System.Collections.Immutable;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// The height layers of a project (user story T-5): <c>layers/layers.json</c> with a PNG per
    /// layer (<see cref="MapProjectLayout.HeightLayers"/>).
    /// </summary>
    public static partial class MapProjectStore
    {
        /// <summary>
        /// The height in game units of a sample 0 in a layer's PNG: adding layers hold differences,
        /// so 0 lies in the middle of the 16 bits.
        /// </summary>
        private static float OffsetOf(HeightLayer layer, float scale) =>
            layer.Combine == HeightCombine.Add ? -32768 * scale : 0;

        /// <summary>
        /// Writes the layers whose values changed since the previous save and the index, and removes
        /// the files of layers gone; a map without layers has no index.
        /// </summary>
        private static void SaveHeightLayers(MapDocument document, string folder, MapDocument? previous)
        {
            string directory = Path.Combine(folder, MapProjectLayout.HeightLayers);
            string index = Path.Combine(folder, MapProjectLayout.HeightLayersIndex);
            float scale = document.Terrain.Heightmap.Scale;
            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (HeightLayer layer in document.HeightLayers.Where(layer => layer.Kind != HeightLayerKind.Curve))
            {
                files.Add(Path.GetFileName(MapProjectLayout.HeightLayer(layer.Name)));
                if (layer.Coverage is not null)
                {
                    files.Add(Path.GetFileName(MapProjectLayout.HeightLayerCoverage(layer.Name)));
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
            }
            if (document.HeightLayers.IsEmpty)
            {
                if (File.Exists(index))
                {
                    File.Delete(index);
                }
                return;
            }

            Directory.CreateDirectory(directory);
            foreach (HeightLayer layer in document.HeightLayers.Where(layer => layer.Kind != HeightLayerKind.Curve))
            {
                HeightLayer? before = previous is null ? null : HeightLayers.Find(previous, layer.Name);
                bool sameEncoding = before is not null && before.Name == layer.Name && OffsetOf(before, scale) == OffsetOf(layer, scale)
                    && previous!.Terrain.Heightmap.Scale == scale;
                string values = Path.Combine(folder, MapProjectLayout.HeightLayer(layer.Name));
                if (!sameEncoding || !ReferenceEquals(before!.Values, layer.Values) || !File.Exists(values))
                {
                    float offset = OffsetOf(layer, scale);
                    float[] heights = layer.Values.ToArray();
                    ushort[] samples = new ushort[heights.Length];
                    for (int i = 0; i < heights.Length; i++)
                    {
                        samples[i] = (ushort)Math.Clamp(MathF.Round((heights[i] - offset) / scale), 0, ushort.MaxValue);
                    }
                    ProjectImages.WriteGray16(values, layer.Values.Width, layer.Values.Height, samples);
                }
                if (layer.Coverage is TiledBuffer<byte> coverage)
                {
                    string file = Path.Combine(folder, MapProjectLayout.HeightLayerCoverage(layer.Name));
                    if (before is null || before.Name != layer.Name || !ReferenceEquals(before.Coverage, coverage) || !File.Exists(file))
                    {
                        WriteGray8(file, coverage);
                    }
                }
            }
            ProjectJson.Write(index, ToDto(document));
        }

        private static HeightLayersDto ToDto(MapDocument document) => new HeightLayersDto
        {
            Layers = [.. document.HeightLayers.Select(layer => new HeightLayerDto
            {
                Name = layer.Name,
                Kind = layer.Kind,
                Combine = layer.Combine,
                Strength = layer.Strength == 1 ? null : layer.Strength,
                Mask = layer.Mask,
                Hidden = layer.Visible ? null : true,
                Locked = layer.Locked ? true : null,
                Offset = layer.Kind == HeightLayerKind.Curve ? null : OffsetOf(layer, document.Terrain.Heightmap.Scale),
                Source = layer.Source,
                Curve = layer.Curve is HeightCurve curve ? new HeightCurveDto
                {
                    Profile = curve.Profile,
                    Width = curve.Width,
                    Falloff = curve.Falloff,
                    Closed = curve.Closed ? true : null,
                    Symmetry = Files.MapProjectMapping.ToDto(curve.Symmetry),
                    Points = [.. curve.Points.Select(point => new[] { point.X, point.Z, point.Height })],
                } : null,
            })],
        };

        /// <summary>
        /// The document with the project's height layers, and its heightmap their sum.
        /// </summary>
        /// <exception cref="MapProjectException">A layer's file is missing or does not fit.</exception>
        private static MapDocument LoadHeightLayers(MapDocument document, string folder)
        {
            if (!File.Exists(Path.Combine(folder, MapProjectLayout.HeightLayersIndex)))
            {
                return document;
            }
            return HeightLayers.Recomposite(document with { HeightLayers = ReadHeightLayers(document, folder) });
        }

        /// <summary>
        /// The layers as the project's files hold them; the values of a layer the document has
        /// already share its tiles where they are the same, so a read that changes nothing gives the
        /// same layer.
        /// </summary>
        private static ImmutableArray<HeightLayer> ReadHeightLayers(MapDocument document, string folder)
        {
            string index = Path.Combine(folder, MapProjectLayout.HeightLayersIndex);
            if (!File.Exists(index))
            {
                return [];
            }
            HeightLayersDto dto = ProjectJson.Read<HeightLayersDto>(index);
            int width = document.Width + 1;
            int height = document.Height + 1;
            float scale = document.Terrain.Heightmap.Scale;
            ImmutableArray<HeightLayer>.Builder layers = ImmutableArray.CreateBuilder<HeightLayer>();
            foreach (HeightLayerDto entry in dto.Layers)
            {
                HeightLayer? held = HeightLayers.Find(document, entry.Name) is HeightLayer found && found.Name == entry.Name ? found : null;
                HeightCurve? curve = entry.Curve is HeightCurveDto c ? new HeightCurve
                {
                    Profile = c.Profile,
                    Width = c.Width,
                    Falloff = c.Falloff,
                    Closed = c.Closed == true,
                    Symmetry = Files.MapProjectMapping.ToModel(c.Symmetry),
                    Points = [.. c.Points.Where(point => point.Length >= 2).Select(point => new CurvePoint(point[0], point[1], point.Length > 2 ? point[2] : 0))],
                } : null;

                TiledBuffer<float> values;
                TiledBuffer<byte>? coverage = null;
                if (entry.Kind == HeightLayerKind.Curve)
                {
                    curve ??= new HeightCurve();
                    if (held?.Curve is HeightCurve same && same.Equals(curve))
                    {
                        (values, coverage) = (held.Values, held.Coverage);
                    }
                    else
                    {
                        (values, coverage) = HeightCurves.Rasterize(curve, document.Width, document.Height, held?.Values, held?.Coverage);
                    }
                }
                else
                {
                    string file = Path.Combine(folder, MapProjectLayout.HeightLayer(entry.Name));
                    ushort[] samples = ProjectImages.ReadGray16(file, width, height);
                    values = HeightLayerEdits.FromSamples(TiledBuffer<ushort>.FromArray(width, height, samples), scale, held?.Values, entry.Offset ?? 0);
                    string coverageFile = Path.Combine(folder, MapProjectLayout.HeightLayerCoverage(entry.Name));
                    if (File.Exists(coverageFile))
                    {
                        TiledBuffer<byte> read = ReadGray8(coverageFile, width, height);
                        coverage = held?.Coverage is TiledBuffer<byte> heldCoverage && read.ToArray().AsSpan().SequenceEqual(heldCoverage.ToArray()) ? heldCoverage : read;
                    }
                }
                HeightLayer layer = new HeightLayer
                {
                    Name = entry.Name,
                    Kind = entry.Kind,
                    Combine = entry.Combine,
                    Strength = Math.Clamp(entry.Strength ?? 1, 0, 1),
                    Mask = entry.Mask,
                    Visible = entry.Hidden != true,
                    Locked = entry.Locked == true,
                    Values = values,
                    Coverage = coverage,
                    Source = entry.Source,
                    Curve = curve,
                };
                layers.Add(held is not null && SameLayer(held, layer) ? held : layer);
            }
            if (layers.Count == 0 || layers[0].Kind != HeightLayerKind.Base || layers.Skip(1).Any(layer => layer.Kind == HeightLayerKind.Base))
            {
                throw new MapProjectException($"{index}: the lowest height layer is the base, and there is one base");
            }
            return layers.ToImmutable();
        }

        private static bool SameLayer(HeightLayer a, HeightLayer b) =>
            a.Name == b.Name && a.Kind == b.Kind && a.Combine == b.Combine && a.Strength == b.Strength && a.Mask == b.Mask
            && a.Visible == b.Visible && a.Locked == b.Locked && a.Source == b.Source && Equals(a.Curve, b.Curve)
            && ReferenceEquals(a.Values, b.Values) && ReferenceEquals(a.Coverage, b.Coverage);

        /// <summary>
        /// The document with the layers read again after a file in <c>layers/</c> changed; the
        /// heightmap follows when the edit is kept.
        /// </summary>
        private static MapDocument ReloadHeightLayers(MapDocument document, string folder)
        {
            ImmutableArray<HeightLayer> layers = ReadHeightLayers(document, folder);
            if (layers.IsEmpty)
            {
                // the index was removed: the layers end, the heightmap stays
                return document.HeightLayers.IsEmpty ? document : document with { HeightLayers = [] };
            }
            return layers.SequenceEqual(document.HeightLayers) ? document : document with { HeightLayers = layers };
        }

        /// <summary>
        /// The imported layer whose source is the changed file, if any.
        /// </summary>
        private static HeightLayer? ImportedLayerOf(MapDocument document, string folder, string changed)
        {
            string full = Path.GetFullPath(Path.Combine(folder, changed));
            return document.HeightLayers.FirstOrDefault(layer => layer.Kind == HeightLayerKind.Imported && layer.Source is string source
                && string.Equals(Path.GetFullPath(Path.Combine(folder, source)), full, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The document with an imported layer read again from its source; a source that is gone
        /// leaves the layer as it is.
        /// </summary>
        private static MapDocument ReloadImported(MapDocument document, string folder, HeightLayer layer)
        {
            string path = Path.GetFullPath(Path.Combine(folder, layer.Source!));
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
