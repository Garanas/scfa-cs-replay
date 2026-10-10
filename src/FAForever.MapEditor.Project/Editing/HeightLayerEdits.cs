using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Changes the stack of height layers (user story T-5, <see cref="MapDocument.HeightLayers"/>):
    /// start it, add, remove, rename and order layers, and set how each combines, how strongly,
    /// where (a mask), whether it shows and whether it is locked.
    /// </summary>
    /// <remarks>
    /// These edits change the layers only; the heightmap follows in the step that keeps them
    /// (<see cref="EditSession"/> runs <see cref="HeightLayers.Follow"/>), or with
    /// <see cref="HeightLayers.Recomposite"/> outside a session. The base is always the lowest layer:
    /// it cannot be moved, removed, masked or weakened. A name is a file name too: letters,
    /// digits, spaces, dashes and underscores, unique without regard to case.
    /// </remarks>
    public static class HeightLayerEdits
    {
        /// <summary>
        /// The longest name a layer may have.
        /// </summary>
        public const int MaxNameLength = 64;

        /// <summary>
        /// The name the base gets when the stack begins.
        /// </summary>
        public const string BaseName = "Base";

        /// <summary>
        /// Why a name cannot be a new layer's name, or null when it can.
        /// </summary>
        /// <param name="except">A layer the name may equal (one being renamed).</param>
        public static string? Problem(MapDocument document, string name, string? except = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "A height layer needs a name";
            }
            if (name.Length > MaxNameLength)
            {
                return $"A height layer's name has at most {MaxNameLength} characters";
            }
            if (name != name.Trim())
            {
                return "A height layer's name does not start or end with a space";
            }
            if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_'))
            {
                return $"'{name}' is no name for a height layer: use letters, digits, spaces, dashes and underscores";
            }
            if (document.HeightLayers.FirstOrDefault(layer => layer.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && layer.Name != except) is HeightLayer taken)
            {
                return $"The map has a height layer '{taken.Name}' already";
            }
            return null;
        }

        /// <summary>
        /// A name no layer has: <paramref name="stem"/>, else with the first free number after it.
        /// </summary>
        public static string FreeName(MapDocument document, string stem)
        {
            if (Problem(document, stem) is null)
            {
                return stem;
            }
            for (int number = 2; ; number++)
            {
                string name = $"{stem} {number}";
                if (Problem(document, name) is null)
                {
                    return name;
                }
            }
        }

        /// <summary>
        /// The document with a stack of layers: the base, holding the heightmap as it is. A document
        /// with layers already is returned as it is.
        /// </summary>
        public static MapDocument Start(MapDocument document)
        {
            if (!document.HeightLayers.IsEmpty)
            {
                return document;
            }
            Heightmap heightmap = document.Terrain.Heightmap;
            HeightLayer layer = new HeightLayer
            {
                Name = BaseName,
                Kind = HeightLayerKind.Base,
                Combine = HeightCombine.Replace,
                Values = FromSamples(heightmap.Samples, heightmap.Scale),
            };
            return document with { HeightLayers = [layer] };
        }

        /// <summary>
        /// The document with a layer added (the stack started when the map has none), above the
        /// layer <paramref name="above"/> or on top.
        /// </summary>
        /// <exception cref="ArgumentException">The name is taken or no name, or the layer is a second base.</exception>
        public static MapDocument Add(MapDocument document, HeightLayer layer, string? above = null)
        {
            document = Start(document);
            if (layer.Kind == HeightLayerKind.Base)
            {
                throw new ArgumentException("A map has one base layer", nameof(layer));
            }
            if (Problem(document, layer.Name) is string problem)
            {
                throw new ArgumentException(problem, nameof(layer));
            }
            CheckSize(document, layer.Values);
            int index = above is null ? document.HeightLayers.Length : IndexOf(document, above) + 1;
            return document with { HeightLayers = document.HeightLayers.Insert(index, layer) };
        }

        /// <summary>
        /// A layer for the sculpt tool: adds nothing until sculpted.
        /// </summary>
        public static HeightLayer NewSculpt(MapDocument document, string name, HeightCombine combine = HeightCombine.Add) => new HeightLayer
        {
            Name = name,
            Kind = HeightLayerKind.Sculpt,
            Combine = combine,
            Values = TiledBuffer<float>.Create(document.Width + 1, document.Height + 1),
            Coverage = combine == HeightCombine.Add ? null : TiledBuffer<byte>.Create(document.Width + 1, document.Height + 1),
        };

        /// <summary>
        /// A layer of another tool's heightmap, already resampled to the map's corners
        /// (<see cref="HeightmapFiles.Import"/> does so), replacing the heights below it.
        /// </summary>
        /// <param name="samples">(width + 1) by (height + 1) samples of the map's height scale.</param>
        /// <param name="source">The file it is read from again.</param>
        public static HeightLayer NewImported(MapDocument document, string name, ushort[] samples, string? source) => new HeightLayer
        {
            Name = name,
            Kind = HeightLayerKind.Imported,
            Combine = HeightCombine.Replace,
            Values = FromSamples(TiledBuffer<ushort>.FromArray(document.Width + 1, document.Height + 1, samples), document.Terrain.Heightmap.Scale),
            Source = source,
        };

        /// <summary>
        /// A layer made from a curve (user story T-4), combining as its profile does
        /// (<see cref="HeightCurves.CombineOf"/>).
        /// </summary>
        public static HeightLayer NewCurve(MapDocument document, string name, HeightCurve curve)
        {
            (TiledBuffer<float> values, TiledBuffer<byte>? coverage) = HeightCurves.Rasterize(curve, document.Width, document.Height);
            return new HeightLayer
            {
                Name = name,
                Kind = HeightLayerKind.Curve,
                Combine = HeightCurves.CombineOf(curve.Profile),
                Values = values,
                Coverage = coverage,
                Curve = curve,
            };
        }

        /// <summary>
        /// The document with a curve layer's curve changed and its values made again from it; only
        /// the tiles that come out differently change.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, it is no curve, or it is locked.</exception>
        public static MapDocument SetCurve(MapDocument document, string name, HeightCurve curve)
        {
            int index = IndexOf(document, name);
            HeightLayer layer = document.HeightLayers[index];
            if (layer.Kind != HeightLayerKind.Curve)
            {
                throw new ArgumentException($"The height layer {layer.Name} is no curve", nameof(name));
            }
            if (layer.Locked)
            {
                throw new ArgumentException($"The height layer {layer.Name} is locked", nameof(name));
            }
            if (Equals(layer.Curve, curve))
            {
                return document;
            }
            (TiledBuffer<float> values, TiledBuffer<byte>? coverage) = HeightCurves.Rasterize(curve, document.Width, document.Height, layer.Values, layer.Coverage);
            // a new profile brings its way of combining, unless the map maker chose another
            HeightCombine combine = layer.Curve is HeightCurve before && before.Profile != curve.Profile && layer.Combine == HeightCurves.CombineOf(before.Profile)
                ? HeightCurves.CombineOf(curve.Profile)
                : layer.Combine;
            return With(document, index, layer with { Curve = curve, Values = values, Coverage = coverage, Combine = combine });
        }

        /// <summary>
        /// The document without a layer. The base stays: <see cref="Bake"/> ends the stack.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or it is the base.</exception>
        public static MapDocument Remove(MapDocument document, string name)
        {
            int index = IndexOf(document, name);
            if (document.HeightLayers[index].Kind == HeightLayerKind.Base)
            {
                throw new ArgumentException("The base layer stays: bake the layers to end the stack", nameof(name));
            }
            return document with { HeightLayers = document.HeightLayers.RemoveAt(index) };
        }

        /// <summary>
        /// The document with a layer at another place in the stack: 1 is just above the base.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, it is the base, or the place is not above the base.</exception>
        public static MapDocument Move(MapDocument document, string name, int index)
        {
            int from = IndexOf(document, name);
            if (document.HeightLayers[from].Kind == HeightLayerKind.Base)
            {
                throw new ArgumentException("The base layer stays the lowest", nameof(name));
            }
            if (index < 1 || index >= document.HeightLayers.Length)
            {
                throw new ArgumentException($"A layer goes to place 1 (just above the base) to {document.HeightLayers.Length - 1}, not {index}", nameof(index));
            }
            HeightLayer layer = document.HeightLayers[from];
            return document with { HeightLayers = document.HeightLayers.RemoveAt(from).Insert(index, layer) };
        }

        /// <summary>
        /// The document with a layer under another name.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or the new name cannot be a layer's.</exception>
        public static MapDocument Rename(MapDocument document, string name, string newName)
        {
            int index = IndexOf(document, name);
            HeightLayer layer = document.HeightLayers[index];
            if (Problem(document, newName, except: layer.Name) is string problem)
            {
                throw new ArgumentException(problem, nameof(newName));
            }
            return With(document, index, layer with { Name = newName });
        }

        /// <summary>
        /// The document with a layer combining another way. A sculpting layer keeps its look: going
        /// from adding to an absolute way (maximum, minimum, replace) its differences become heights
        /// over the layers below where it changed anything, and back again.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or it is the base.</exception>
        public static MapDocument SetCombine(MapDocument document, string name, HeightCombine combine)
        {
            int index = IndexOf(document, name);
            HeightLayer layer = document.HeightLayers[index];
            if (layer.Kind == HeightLayerKind.Base)
            {
                throw new ArgumentException("The base layer replaces: it is the lowest", nameof(name));
            }
            if (layer.Combine == combine)
            {
                return document;
            }
            bool wasAdd = layer.Combine == HeightCombine.Add;
            bool isAdd = combine == HeightCombine.Add;
            if (layer.Kind != HeightLayerKind.Sculpt || wasAdd == isAdd)
            {
                return With(document, index, layer with { Combine = combine });
            }
            return With(document, index, Converted(document, index, layer, combine));
        }

        /// <summary>
        /// The document with a layer working more or less strongly, 0 to 1.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or it is the base.</exception>
        public static MapDocument SetStrength(MapDocument document, string name, float strength)
        {
            int index = IndexOf(document, name);
            HeightLayer layer = document.HeightLayers[index];
            if (layer.Kind == HeightLayerKind.Base)
            {
                throw new ArgumentException("The base layer works at full strength", nameof(name));
            }
            return With(document, index, layer with { Strength = Math.Clamp(strength, 0, 1) });
        }

        /// <summary>
        /// The document with a layer working only where a mask is (<see cref="HeightLayers.MaskKey"/>,
        /// <see cref="HeightLayers.StratumKey"/>), or everywhere (null).
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer or mask, or the layer is the base.</exception>
        public static MapDocument SetMask(MapDocument document, string name, string? mask)
        {
            int index = IndexOf(document, name);
            HeightLayer layer = document.HeightLayers[index];
            if (layer.Kind == HeightLayerKind.Base && mask is not null)
            {
                throw new ArgumentException("The base layer works everywhere", nameof(name));
            }
            if (mask is not null && HeightLayers.MaskOf(document, mask) is null)
            {
                throw new ArgumentException($"The map has no {HeightLayers.MaskTitle(mask)}", nameof(mask));
            }
            return With(document, index, layer with { Mask = mask });
        }

        public static MapDocument SetVisible(MapDocument document, string name, bool visible)
        {
            int index = IndexOf(document, name);
            return With(document, index, document.HeightLayers[index] with { Visible = visible });
        }

        public static MapDocument SetLocked(MapDocument document, string name, bool locked)
        {
            int index = IndexOf(document, name);
            return With(document, index, document.HeightLayers[index] with { Locked = locked });
        }

        /// <summary>
        /// The document with an imported layer's values read again from its file.
        /// </summary>
        /// <param name="samples">(width + 1) by (height + 1) samples of the map's height scale.</param>
        /// <exception cref="ArgumentException">There is no such layer, or it is not imported.</exception>
        public static MapDocument SetImported(MapDocument document, string name, ushort[] samples, string? source = null)
        {
            int index = IndexOf(document, name);
            HeightLayer layer = document.HeightLayers[index];
            if (layer.Kind != HeightLayerKind.Imported)
            {
                throw new ArgumentException($"The height layer {layer.Name} is not imported", nameof(name));
            }
            TiledBuffer<float> values = FromSamples(TiledBuffer<ushort>.FromArray(document.Width + 1, document.Height + 1, samples), document.Terrain.Heightmap.Scale, layer.Values);
            return With(document, index, layer with { Values = values, Source = source ?? layer.Source });
        }

        /// <summary>
        /// The document with the base's heights replaced, e.g. by a heightmap another tool saved
        /// over the project's.
        /// </summary>
        /// <param name="samples">(width + 1) by (height + 1) samples.</param>
        public static MapDocument SetBase(MapDocument document, TiledBuffer<ushort> samples)
        {
            document = Start(document);
            HeightLayer layer = document.HeightLayers[0];
            return With(document, 0, layer with { Values = FromSamples(samples, document.Terrain.Heightmap.Scale, layer.Values) });
        }

        /// <summary>
        /// Ends the stack: the heightmap keeps what the layers add up to, and the map has no layers.
        /// </summary>
        public static MapDocument Bake(MapDocument document) =>
            document.HeightLayers.IsEmpty ? document : document with { HeightLayers = [] };

        /// <summary>
        /// The place of a layer in the stack, by name (exactly, else without regard to case).
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer.</exception>
        public static int IndexOf(MapDocument document, string name)
        {
            HeightLayer layer = HeightLayers.Find(document, name)
                ?? throw new ArgumentException(document.HeightLayers.IsEmpty
                    ? "The map has no height layers"
                    : $"The map has no height layer '{name}': {string.Join(", ", document.HeightLayers.Select(each => each.Name))}", nameof(name));
            return document.HeightLayers.IndexOf(layer);
        }

        internal static MapDocument With(MapDocument document, int index, HeightLayer layer) =>
            ReferenceEquals(document.HeightLayers[index], layer) ? document : document with { HeightLayers = document.HeightLayers.SetItem(index, layer) };

        /// <summary>
        /// Heights in game units from samples; with <paramref name="reuse"/>, its tiles where they
        /// hold the same values, so that only the tiles that differ are added up again.
        /// </summary>
        internal static TiledBuffer<float> FromSamples(TiledBuffer<ushort> samples, float scale, TiledBuffer<float>? reuse = null, float offset = 0)
        {
            if (reuse is not null && (reuse.Width != samples.Width || reuse.Height != samples.Height))
            {
                reuse = null;
            }
            const int size = TiledBuffer<float>.TileSize;
            TiledBuffer<float>.Builder builder = (reuse ?? TiledBuffer<float>.Create(samples.Width, samples.Height)).ToBuilder();
            float[] tile = new float[size * size];
            for (int tileY = 0; tileY < samples.TilesY; tileY++)
            {
                for (int tileX = 0; tileX < samples.TilesX; tileX++)
                {
                    ReadOnlySpan<ushort> from = samples.Tile(tileX, tileY);
                    for (int i = 0; i < tile.Length; i++)
                    {
                        tile[i] = offset + from[i] * scale;
                    }
                    if (reuse is null || !HeightLayers.SameTile(samples, tileX, tileY, tile, builder.Tile(tileX, tileY)))
                    {
                        builder.SetTile(tileX, tileY, tile);
                    }
                }
            }
            return builder.ToBuffer();
        }

        /// <summary>
        /// A sculpting layer turned from adding into an absolute way of combining, or back, with the
        /// heights of the layers below it as they are.
        /// </summary>
        private static HeightLayer Converted(MapDocument document, int index, HeightLayer layer, HeightCombine combine)
        {
            const int size = TiledBuffer<float>.TileSize;
            bool toAdd = combine == HeightCombine.Add;
            TiledBuffer<float>.Builder values = layer.Values.ToBuilder();
            TiledBuffer<byte>? oldCoverage = layer.Coverage;
            TiledBuffer<byte>.Builder? coverage = toAdd ? null : TiledBuffer<byte>.Create(layer.Values.Width, layer.Values.Height).ToBuilder();
            float[] below = new float[size * size];
            float[] tile = new float[size * size];
            float[] zeros = new float[size * size];
            byte[] covered = new byte[size * size];
            for (int tileY = 0; tileY < layer.Values.TilesY; tileY++)
            {
                for (int tileX = 0; tileX < layer.Values.TilesX; tileX++)
                {
                    ReadOnlySpan<float> held = layer.Values.Tile(tileX, tileY);
                    ReadOnlySpan<byte> heldCoverage = oldCoverage is null ? default : oldCoverage.Tile(tileX, tileY);
                    bool any = false;
                    for (int i = 0; i < held.Length && !any; i++)
                    {
                        any = toAdd ? (oldCoverage is null || heldCoverage[i] > 0) : held[i] != 0;
                    }
                    if (!any)
                    {
                        if (toAdd && !held.SequenceEqual(zeros))
                        {
                            Array.Clear(tile);
                            values.SetTile(tileX, tileY, tile);
                        }
                        continue;
                    }
                    HeightLayers.Heights(document, tileX, tileY, index, below);
                    for (int i = 0; i < tile.Length; i++)
                    {
                        if (toAdd)
                        {
                            float weight = oldCoverage is null ? 1 : heldCoverage[i] / 255f;
                            float difference = held[i] - below[i];
                            difference = layer.Combine switch
                            {
                                HeightCombine.Maximum => Math.Max(0, difference),
                                HeightCombine.Minimum => Math.Min(0, difference),
                                _ => difference,
                            };
                            tile[i] = weight > 0 ? difference * weight : 0;
                        }
                        else
                        {
                            covered[i] = held[i] != 0 ? (byte)255 : (byte)0;
                            tile[i] = below[i] + held[i];
                        }
                    }
                    values.SetTile(tileX, tileY, tile);
                    if (coverage is not null)
                    {
                        coverage.SetTile(tileX, tileY, covered);
                    }
                }
            }
            return layer with { Combine = combine, Values = values.ToBuffer(), Coverage = coverage?.ToBuffer() };
        }

        private static void CheckSize(MapDocument document, TiledBuffer<float> values)
        {
            if (values.Width != document.Width + 1 || values.Height != document.Height + 1)
            {
                throw new ArgumentException($"A height layer has {document.Width + 1} by {document.Height + 1} values, one per corner; this one {values.Width} by {values.Height}");
            }
        }
    }
}
