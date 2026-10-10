using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Changes the heightmap's stack of layers (user story T-5, <see cref="MapDocument.HeightStack"/>):
    /// start it, add, remove, rename and order layers, and set how each combines, how strongly,
    /// where (a mask), whether it shows and whether it is locked. The stack's own changes are
    /// <see cref="LayerStackEdits"/>'s; these add the heightmap's rules and its kinds of layer
    /// (painted for sculpting, image, curve).
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
        public const int MaxNameLength = LayerStackEdits.MaxNameLength;

        /// <summary>
        /// The name the base gets when the stack begins.
        /// </summary>
        public const string BaseName = "Base";

        /// <summary>
        /// What the messages call a layer of the heightmap.
        /// </summary>
        private const string What = "height layer";

        /// <summary>
        /// Why a name cannot be a new layer's name, or null when it can.
        /// </summary>
        /// <param name="except">A layer the name may equal (one being renamed).</param>
        public static string? Problem(MapDocument document, string name, string? except = null) =>
            LayerStackEdits.NameProblem(document.HeightStack, name, except, What);

        /// <summary>
        /// A name no layer has: <paramref name="stem"/>, else with the first free number after it.
        /// </summary>
        public static string FreeName(MapDocument document, string stem) => LayerStackEdits.FreeName(document.HeightStack, stem);

        /// <summary>
        /// The document with a stack of layers: the base, holding the heightmap as it is. A document
        /// with layers already is returned as it is.
        /// </summary>
        public static MapDocument Start(MapDocument document)
        {
            if (!document.HeightStack.IsEmpty)
            {
                return document;
            }
            Heightmap heightmap = document.Terrain.Heightmap;
            Layer layer = new Layer
            {
                Name = BaseName,
                Source = LayerSource.Painted,
                IsBase = true,
                Combine = LayerCombine.Replace,
                Values = FromSamples(heightmap.Samples, heightmap.Scale),
            };
            return document with { HeightStack = document.HeightStack with { Layers = [layer] } };
        }

        /// <summary>
        /// The document with a layer added (the stack started when the map has none), above the
        /// layer <paramref name="above"/> or on top.
        /// </summary>
        /// <exception cref="ArgumentException">The name is taken or no name, or the layer is a second base.</exception>
        public static MapDocument Add(MapDocument document, Layer layer, string? above = null)
        {
            document = Start(document);
            if (layer.IsBase)
            {
                throw new ArgumentException("A map has one base layer", nameof(layer));
            }
            if (layer.Values is TiledBuffer<float> values)
            {
                CheckSize(document, values);
            }
            return With(document, LayerStackEdits.Add(document.HeightStack, layer, above, What));
        }

        /// <summary>
        /// A layer for the sculpt tool: adds nothing until sculpted.
        /// </summary>
        public static Layer NewSculpt(MapDocument document, string name, LayerCombine combine = LayerCombine.Add) => new Layer
        {
            Name = name,
            Source = LayerSource.Painted,
            Combine = combine,
            Values = TiledBuffer<float>.Create(document.Width + 1, document.Height + 1),
            Coverage = IsDifference(combine) ? null : TiledBuffer<byte>.Create(document.Width + 1, document.Height + 1),
        };

        /// <summary>
        /// A layer of another tool's heightmap, already resampled to the map's corners
        /// (<see cref="HeightmapFiles.Import"/> does so), replacing the heights below it.
        /// </summary>
        /// <param name="samples">(width + 1) by (height + 1) samples of the map's height scale.</param>
        /// <param name="source">The file it is read from again.</param>
        public static Layer NewImported(MapDocument document, string name, ushort[] samples, string source) => new Layer
        {
            Name = name,
            Source = new ImageSource(source),
            Combine = LayerCombine.Replace,
            Values = FromSamples(TiledBuffer<ushort>.FromArray(document.Width + 1, document.Height + 1, samples), document.Terrain.Heightmap.Scale),
        };

        /// <summary>
        /// A layer made from a curve (user story T-4), combining as its profile does
        /// (<see cref="HeightCurves.CombineOf"/>).
        /// </summary>
        public static Layer NewCurve(MapDocument document, string name, HeightCurve curve)
        {
            (TiledBuffer<float> values, TiledBuffer<byte>? coverage) = HeightCurves.Rasterize(curve, document.Width, document.Height);
            return new Layer
            {
                Name = name,
                Source = new CurveSource(curve),
                Combine = HeightCurves.CombineOf(curve.Profile),
                Values = values,
                Coverage = coverage,
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
            Layer layer = document.HeightStack.Layers[index];
            if (layer.Curve is not HeightCurve before)
            {
                throw new ArgumentException($"The height layer {layer.Name} is no curve", nameof(name));
            }
            if (layer.Locked)
            {
                throw new ArgumentException($"The height layer {layer.Name} is locked", nameof(name));
            }
            if (Equals(before, curve))
            {
                return document;
            }
            (TiledBuffer<float> values, TiledBuffer<byte>? coverage) = HeightCurves.Rasterize(curve, document.Width, document.Height, layer.Values, layer.Coverage, before);
            // a new profile brings its way of combining, unless the map maker chose another
            LayerCombine combine = before.Profile != curve.Profile && layer.Combine == HeightCurves.CombineOf(before.Profile)
                ? HeightCurves.CombineOf(curve.Profile)
                : layer.Combine;
            return With(document, index, layer with { Source = new CurveSource(curve), Values = values, Coverage = coverage, Combine = combine });
        }

        /// <summary>
        /// The document without a layer. The base stays: <see cref="Bake"/> ends the stack.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or it is the base.</exception>
        public static MapDocument Remove(MapDocument document, string name)
        {
            if (document.HeightStack.Layers[IndexOf(document, name)].IsBase)
            {
                throw new ArgumentException("The base layer stays: bake the layers to end the stack", nameof(name));
            }
            return With(document, LayerStackEdits.Remove(document.HeightStack, name, What));
        }

        /// <summary>
        /// The document with a layer at another place in the stack: 1 is just above the base.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, it is the base, or the place is not above the base.</exception>
        public static MapDocument Move(MapDocument document, string name, int index)
        {
            if (document.HeightStack.Layers[IndexOf(document, name)].IsBase)
            {
                throw new ArgumentException("The base layer stays the lowest", nameof(name));
            }
            return With(document, LayerStackEdits.Move(document.HeightStack, name, index, What));
        }

        /// <summary>
        /// The document with a layer under another name.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or the new name cannot be a layer's.</exception>
        public static MapDocument Rename(MapDocument document, string name, string newName) =>
            With(document, LayerStackEdits.Rename(document.HeightStack, name, newName, What));

        /// <summary>
        /// The document with a layer combining another way. A sculpting layer keeps its look: going
        /// from differences (add, subtract) to an absolute way (maximum, minimum, replace, multiply)
        /// its differences become heights over the layers below where it changed anything, and back
        /// again.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or it is the base.</exception>
        public static MapDocument SetCombine(MapDocument document, string name, LayerCombine combine)
        {
            int index = IndexOf(document, name);
            Layer layer = document.HeightStack.Layers[index];
            if (layer.IsBase)
            {
                throw new ArgumentException("The base layer replaces: it is the lowest", nameof(name));
            }
            if (layer.Combine == combine)
            {
                return document;
            }
            if (layer.Source is PaintedSource && IsDifference(layer.Combine) && IsDifference(combine) && layer.Values is TiledBuffer<float> differences)
            {
                // adding and subtracting: the differences turn round
                return With(document, index, layer with { Combine = combine, Values = Negated(differences) });
            }
            if (layer.Source is not PaintedSource || IsDifference(layer.Combine) == IsDifference(combine) || combine == LayerCombine.Multiply || layer.Combine == LayerCombine.Multiply)
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
            if (document.HeightStack.Layers[IndexOf(document, name)].IsBase)
            {
                throw new ArgumentException("The base layer works at full strength", nameof(name));
            }
            return With(document, LayerStackEdits.SetStrength(document.HeightStack, name, strength, What));
        }

        /// <summary>
        /// The document with a layer working only where a mask is (<see cref="LayerStacks.MaskKey"/>,
        /// <see cref="LayerStacks.StratumKey"/>), or everywhere (null).
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer or mask, or the layer is the base.</exception>
        public static MapDocument SetMask(MapDocument document, string name, string? mask)
        {
            if (document.HeightStack.Layers[IndexOf(document, name)].IsBase && mask is not null)
            {
                throw new ArgumentException("The base layer works everywhere", nameof(name));
            }
            if (mask is not null && LayerStacks.MaskOf(document, mask) is null)
            {
                throw new ArgumentException($"The map has no {LayerStacks.MaskTitle(mask)}", nameof(mask));
            }
            return With(document, LayerStackEdits.SetClip(document.HeightStack, name, mask, What));
        }

        public static MapDocument SetVisible(MapDocument document, string name, bool visible) =>
            With(document, LayerStackEdits.SetVisible(document.HeightStack, name, visible, What));

        public static MapDocument SetLocked(MapDocument document, string name, bool locked) =>
            With(document, LayerStackEdits.SetLocked(document.HeightStack, name, locked, What));

        /// <summary>
        /// The document with an image layer's values read again from its file.
        /// </summary>
        /// <param name="samples">(width + 1) by (height + 1) samples of the map's height scale.</param>
        /// <exception cref="ArgumentException">There is no such layer, or it is not read from a file.</exception>
        public static MapDocument SetImported(MapDocument document, string name, ushort[] samples, string? source = null)
        {
            int index = IndexOf(document, name);
            Layer layer = document.HeightStack.Layers[index];
            if (layer.Source is not ImageSource image)
            {
                throw new ArgumentException($"The height layer {layer.Name} is not imported", nameof(name));
            }
            TiledBuffer<float> values = FromSamples(TiledBuffer<ushort>.FromArray(document.Width + 1, document.Height + 1, samples), document.Terrain.Heightmap.Scale, layer.Values);
            LayerSource read = source is null || source == image.Path ? image : new ImageSource(source);
            return With(document, index, layer with { Values = values, Source = read });
        }

        /// <summary>
        /// The document with the base's heights replaced, e.g. by a heightmap another tool saved
        /// over the project's.
        /// </summary>
        /// <param name="samples">(width + 1) by (height + 1) samples.</param>
        public static MapDocument SetBase(MapDocument document, TiledBuffer<ushort> samples)
        {
            document = Start(document);
            Layer layer = document.HeightStack.Layers[0];
            return With(document, 0, layer with { Values = FromSamples(samples, document.Terrain.Heightmap.Scale, layer.Values) });
        }

        /// <summary>
        /// Ends the stack: the heightmap keeps what the layers make, and the map has no layers.
        /// </summary>
        public static MapDocument Bake(MapDocument document) =>
            document.HeightStack.IsEmpty ? document : document with { HeightStack = LayerStack.Empty(LayerTarget.Heightmap) };

        /// <summary>
        /// The place of a layer in the stack, by name (exactly, else without regard to case).
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer.</exception>
        public static int IndexOf(MapDocument document, string name) => LayerStackEdits.IndexOf(document.HeightStack, name, What);

        internal static MapDocument With(MapDocument document, int index, Layer layer) =>
            With(document, document.HeightStack.With(index, layer));

        internal static MapDocument With(MapDocument document, LayerStack stack) =>
            ReferenceEquals(document.HeightStack, stack) ? document : document with { HeightStack = stack };

        /// <summary>
        /// Whether a layer combining so holds differences (0 changes nothing) rather than heights.
        /// </summary>
        public static bool IsDifference(LayerCombine combine) => combine is LayerCombine.Add or LayerCombine.Subtract;

        /// <summary>
        /// Heights in game units from samples; with <paramref name="reuse"/>, its tiles where they
        /// hold the same values, so that only the tiles that differ are composited again.
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
                    if (reuse is null || !LayerStacks.SameTile(samples, tileX, tileY, tile, builder.Tile(tileX, tileY)))
                    {
                        builder.SetTile(tileX, tileY, tile);
                    }
                }
            }
            return builder.ToBuffer();
        }

        /// <summary>
        /// A sculpting layer turned from differences (add, subtract) into an absolute way of
        /// combining (maximum, minimum, replace), or back, with the heights of the layers below it as
        /// they are.
        /// </summary>
        private static Layer Converted(MapDocument document, int index, Layer layer, LayerCombine combine)
        {
            const int size = TiledBuffer<float>.TileSize;
            bool toDifference = IsDifference(combine);
            float sign = combine == LayerCombine.Subtract ? -1 : 1;
            float wasSign = layer.Combine == LayerCombine.Subtract ? -1 : 1;
            TiledBuffer<float> held = layer.Values!;
            TiledBuffer<float>.Builder values = held.ToBuilder();
            TiledBuffer<byte>? oldCoverage = layer.Coverage;
            TiledBuffer<byte>.Builder? coverage = toDifference ? null : TiledBuffer<byte>.Create(held.Width, held.Height).ToBuilder();
            float[] below = new float[size * size];
            float[] tile = new float[size * size];
            float[] zeros = new float[size * size];
            byte[] covered = new byte[size * size];
            for (int tileY = 0; tileY < held.TilesY; tileY++)
            {
                for (int tileX = 0; tileX < held.TilesX; tileX++)
                {
                    ReadOnlySpan<float> heldTile = held.Tile(tileX, tileY);
                    ReadOnlySpan<byte> heldCoverage = oldCoverage is null ? default : oldCoverage.Tile(tileX, tileY);
                    bool any = false;
                    for (int i = 0; i < heldTile.Length && !any; i++)
                    {
                        any = toDifference ? (oldCoverage is null || heldCoverage[i] > 0) : heldTile[i] != 0;
                    }
                    if (!any)
                    {
                        if (toDifference && !heldTile.SequenceEqual(zeros))
                        {
                            Array.Clear(tile);
                            values.SetTile(tileX, tileY, tile);
                        }
                        continue;
                    }
                    HeightLayers.Heights(document, tileX, tileY, index, below);
                    for (int i = 0; i < tile.Length; i++)
                    {
                        if (toDifference)
                        {
                            float weight = oldCoverage is null ? 1 : heldCoverage[i] / 255f;
                            float difference = heldTile[i] - below[i];
                            difference = layer.Combine switch
                            {
                                LayerCombine.Maximum => Math.Max(0, difference),
                                LayerCombine.Minimum => Math.Min(0, difference),
                                _ => difference,
                            };
                            tile[i] = weight > 0 ? sign * difference * weight : 0;
                        }
                        else
                        {
                            covered[i] = heldTile[i] != 0 ? (byte)255 : (byte)0;
                            tile[i] = below[i] + wasSign * heldTile[i];
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

        private static TiledBuffer<float> Negated(TiledBuffer<float> values)
        {
            const int size = TiledBuffer<float>.TileSize;
            TiledBuffer<float>.Builder builder = values.ToBuilder();
            float[] tile = new float[size * size];
            for (int tileY = 0; tileY < values.TilesY; tileY++)
            {
                for (int tileX = 0; tileX < values.TilesX; tileX++)
                {
                    ReadOnlySpan<float> held = values.Tile(tileX, tileY);
                    if (!held.ContainsAnyExcept(0f))
                    {
                        continue;
                    }
                    for (int i = 0; i < tile.Length; i++)
                    {
                        tile[i] = -held[i];
                    }
                    builder.SetTile(tileX, tileY, tile);
                }
            }
            return builder.ToBuffer();
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
