using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Changes the heightmap's stacks of layers (user story T-5, <see cref="MapDocument.HeightStack"/>,
    /// the final, and <see cref="MapDocument.DraftStack"/>, the draft; <see cref="HeightStacks"/>):
    /// start them, add, remove, rename and order layers, and set how each combines, how strongly,
    /// where (a mask), whether it shows and whether it is locked. The stack's own changes are
    /// <see cref="LayerStackEdits"/>'s; these add the heightmap's rules and its kinds of layer
    /// (painted for sculpting, image, curve).
    /// </summary>
    /// <remarks>
    /// <para>
    /// These edits change the layers only; the heightmap follows in the step that keeps them
    /// (<see cref="EditSession"/> runs <see cref="HeightLayers.Follow"/>), or with
    /// <see cref="HeightLayers.Recomposite"/> outside a session. The base is always the lowest layer:
    /// it cannot be moved, removed, masked or weakened. A name is a file name too: letters,
    /// digits, spaces, dashes and underscores, unique over both stacks without regard to case.
    /// </para>
    /// <para>
    /// A layer is found by name in either stack. An edit of a layer's content (its source, grids,
    /// way of combining, strength, mask, lock or name) is made in both stacks when the layer is
    /// shared; where it shows (<see cref="SetVisible"/>) and its place (<see cref="Move"/>) are each
    /// stack's own, the final's unless another is named.
    /// </para>
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
            HeightStacks.NameProblem(document, name, except, What);

        /// <summary>
        /// A name no layer has: <paramref name="stem"/>, else with the first free number after it.
        /// </summary>
        public static string FreeName(MapDocument document, string stem) => HeightStacks.FreeName(document, stem);

        /// <summary>
        /// The document with stacks of layers: the base, holding the heightmap as it is, shared by the
        /// draft and the final. A document with layers already is returned as it is.
        /// </summary>
        public static MapDocument Start(MapDocument document)
        {
            if (!document.HeightStack.IsEmpty)
            {
                return HeightStacks.Normalized(document);
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
            LayerStack stack = document.HeightStack with { Layers = [layer] };
            return document with { HeightStack = stack, DraftStack = stack, DraftSamples = null };
        }

        /// <summary>
        /// The document with a layer added (the stacks started when the map has none), above the
        /// layer <paramref name="above"/> or on top, in the stacks <paramref name="use"/> names (left
        /// out: <see cref="HeightStacks.DefaultUse"/> for the stack <paramref name="shown"/>). In a
        /// stack without <paramref name="above"/> it goes on top.
        /// </summary>
        /// <exception cref="ArgumentException">The name is taken or no name, or the layer is a second base.</exception>
        public static MapDocument Add(MapDocument document, Layer layer, string? above = null, HeightStackKind shown = HeightStackKind.Final, LayerUse? use = null)
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
            if (Problem(document, layer.Name) is string problem)
            {
                throw new ArgumentException(problem, nameof(layer));
            }
            LayerUse into = use ?? HeightStacks.DefaultUse(document, shown);
            foreach (HeightStackKind kind in new[] { HeightStackKind.Final, HeightStackKind.Draft })
            {
                if (HeightStacks.IsIn(into, kind))
                {
                    LayerStack stack = HeightStacks.Of(document, kind);
                    string? below = above is not null && stack.Find(above) is not null ? above : null;
                    document = HeightStacks.With(document, kind, LayerStackEdits.Add(stack, layer, below, What));
                }
            }
            return document;
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
                Combine = HeightCurves.CombineOf(curve),
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
            (HeightStackKind kind, int index) = HeightStacks.Locate(document, name);
            Layer layer = HeightStacks.Of(document, kind).Layers[index];
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
            // a new profile or road mode brings its way of combining, unless the map maker chose another
            LayerCombine combine = layer.Combine == HeightCurves.CombineOf(before) ? HeightCurves.CombineOf(curve) : layer.Combine;
            return HeightStacks.WithLayer(document, kind, index, layer with { Source = new CurveSource(curve), Values = values, Coverage = coverage, Combine = combine });
        }

        /// <summary>
        /// The curve of a curve layer.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or it is no curve.</exception>
        public static HeightCurve CurveOf(MapDocument document, string name)
        {
            (HeightStackKind kind, int index) = HeightStacks.Locate(document, name);
            Layer layer = HeightStacks.Of(document, kind).Layers[index];
            return layer.Curve ?? throw new ArgumentException($"The height layer {layer.Name} is no curve", nameof(name));
        }

        /// <summary>
        /// The document with a curve running the other way (<see cref="HeightCurves.Reversed"/>): a
        /// cliff's high side flips, the other profiles keep their look.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, it is no curve, or it is locked.</exception>
        public static MapDocument ReverseCurve(MapDocument document, string name) =>
            SetCurve(document, name, HeightCurves.Reversed(CurveOf(document, name)));

        /// <summary>
        /// The document with one point of a curve set (its place, height and its own values: the
        /// node table's edit).
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer or point, it is no curve, or it is locked.</exception>
        public static MapDocument SetCurvePoint(MapDocument document, string name, int index, CurvePoint point)
        {
            HeightCurve curve = CurveOf(document, name);
            if (index < 0 || index >= curve.Points.Length)
            {
                throw new ArgumentException($"The curve {name} has points 0 to {curve.Points.Length - 1}, not {index}", nameof(index));
            }
            if (point.Strength is < 0 or > 1 || point.Width is < 0 || point.FalloffLeft is < 0 || point.FalloffRight is < 0)
            {
                throw new ArgumentException("A point's width and falloffs are 0 or more ogrids, its strength 0 to 1", nameof(point));
            }
            return SetCurve(document, name, curve with { Points = curve.Points.SetItem(index, point) });
        }

        /// <summary>
        /// The document with the grade between two points of a curve made even
        /// (<see cref="HeightCurves.EvenGrade"/>): the points between them get heights on a straight
        /// line by the distance along the spline.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer or point, it is no curve, or it is locked.</exception>
        public static MapDocument EvenGrade(MapDocument document, string name, int from, int to) =>
            SetCurve(document, name, HeightCurves.EvenGrade(CurveOf(document, name), from, to));

        /// <summary>
        /// The document with points of a road or river put on the ground: each gets the height the
        /// layers below the curve's layer make at its place (in the stack that holds it, the final's
        /// first). An explicit snap: the points do not follow the ground afterwards.
        /// </summary>
        /// <param name="indices">The points to snap; null for all of them.</param>
        /// <exception cref="ArgumentException">There is no such layer or point, it is no road or river, or it is locked.</exception>
        public static MapDocument SnapCurve(MapDocument document, string name, IReadOnlyCollection<int>? indices = null)
        {
            (HeightStackKind kind, int index) = HeightStacks.Locate(document, name);
            LayerStack stack = HeightStacks.Of(document, kind);
            HeightCurve curve = CurveOf(document, name);
            if (!HeightCurves.IsAbsolute(curve.Profile))
            {
                throw new ArgumentException($"Only a road's or river's points lie on the ground; {stack.Layers[index].Name} is a {curve.Profile.ToString().ToLowerInvariant()}", nameof(name));
            }
            if (indices is not null && indices.Any(i => i < 0 || i >= curve.Points.Length))
            {
                throw new ArgumentException($"The curve {name} has points 0 to {curve.Points.Length - 1}", nameof(indices));
            }
            Func<float, float, float> ground = GroundBelow(document, stack, index);
            var points = curve.Points.Select((point, i) => indices is null || indices.Contains(i) ? point with { Height = ground(point.X, point.Z) } : point);
            return SetCurve(document, name, curve with { Points = [.. points] });
        }

        /// <summary>
        /// The height the layers below <paramref name="upTo"/> make at a place (between the corners),
        /// in game units; each tile worked out once.
        /// </summary>
        private static Func<float, float, float> GroundBelow(MapDocument document, LayerStack stack, int upTo)
        {
            const int size = TiledBuffer<float>.TileSize;
            Dictionary<(int, int), float[]> tiles = [];
            int width = document.Width + 1;
            int height = document.Height + 1;
            float Corner(int x, int z)
            {
                x = Math.Clamp(x, 0, width - 1);
                z = Math.Clamp(z, 0, height - 1);
                if (!tiles.TryGetValue((x / size, z / size), out float[]? tile))
                {
                    tile = new float[size * size];
                    LayerStacks.TileOf(document, stack, x / size, z / size, upTo, tile);
                    tiles[(x / size, z / size)] = tile;
                }
                return tile[z % size * size + x % size];
            }
            return (x, z) =>
            {
                int x0 = (int)MathF.Floor(x), z0 = (int)MathF.Floor(z);
                float fx = x - x0, fz = z - z0;
                float top = Corner(x0, z0) + (Corner(x0 + 1, z0) - Corner(x0, z0)) * fx;
                float bottom = Corner(x0, z0 + 1) + (Corner(x0 + 1, z0 + 1) - Corner(x0, z0 + 1)) * fx;
                return top + (bottom - top) * fz;
            };
        }

        /// <summary>
        /// The document without a layer, in both stacks. A base stays: <see cref="Bake"/> ends the
        /// stacks, <see cref="HeightStacks.ShareDraftBase"/> drops the final's own base.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or it is a base.</exception>
        public static MapDocument Remove(MapDocument document, string name)
        {
            (HeightStackKind kind, int index) = HeightStacks.Locate(document, name);
            Layer layer = HeightStacks.Of(document, kind).Layers[index];
            if (layer.IsBase)
            {
                throw new ArgumentException("The base layer stays: bake the layers to end the stack", nameof(name));
            }
            foreach (HeightStackKind each in new[] { HeightStackKind.Final, HeightStackKind.Draft })
            {
                LayerStack stack = HeightStacks.Of(document, each);
                if (stack.Find(layer.Name) is not null)
                {
                    document = HeightStacks.With(document, each, LayerStackEdits.Remove(stack, layer.Name, What));
                }
            }
            return document;
        }

        /// <summary>
        /// The document with a layer at another place in a stack (the final unless the layer is only
        /// in the draft, or <paramref name="kind"/> names the draft): 1 is just above the base.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, it is the base, or the place is not above the base.</exception>
        public static MapDocument Move(MapDocument document, string name, int index, HeightStackKind kind = HeightStackKind.Final)
        {
            (HeightStackKind found, int from) = HeightStacks.Locate(document, name, kind);
            if (HeightStacks.Of(document, found).Layers[from].IsBase)
            {
                throw new ArgumentException("The base layer stays the lowest", nameof(name));
            }
            return HeightStacks.Move(document, found, name, index);
        }

        /// <summary>
        /// The document with a layer under another name, in both stacks.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or the new name cannot be a layer's.</exception>
        public static MapDocument Rename(MapDocument document, string name, string newName)
        {
            (HeightStackKind kind, int index) = HeightStacks.Locate(document, name);
            Layer layer = HeightStacks.Of(document, kind).Layers[index];
            if (Problem(document, newName, except: layer.Name) is string problem)
            {
                throw new ArgumentException(problem, nameof(newName));
            }
            return HeightStacks.WithLayer(document, kind, index, layer with { Name = newName });
        }

        /// <summary>
        /// The document with a layer combining another way. A sculpting layer keeps its look (in the
        /// stack <paramref name="kind"/> when it is in both): going from differences (add, subtract)
        /// to an absolute way (maximum, minimum, replace, multiply) its differences become heights
        /// over the layers below where it changed anything, and back again.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or it is the base.</exception>
        public static MapDocument SetCombine(MapDocument document, string name, LayerCombine combine, HeightStackKind kind = HeightStackKind.Final)
        {
            (HeightStackKind found, int index) = HeightStacks.Locate(document, name, kind);
            Layer layer = HeightStacks.Of(document, found).Layers[index];
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
                return HeightStacks.WithLayer(document, found, index, layer with { Combine = combine, Values = Negated(differences) });
            }
            if (layer.Source is not PaintedSource || IsDifference(layer.Combine) == IsDifference(combine) || combine == LayerCombine.Multiply || layer.Combine == LayerCombine.Multiply)
            {
                return HeightStacks.WithLayer(document, found, index, layer with { Combine = combine });
            }
            return HeightStacks.WithLayer(document, found, index, Converted(document, found, index, layer, combine));
        }

        /// <summary>
        /// The document with a layer working more or less strongly, 0 to 1.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or it is the base.</exception>
        public static MapDocument SetStrength(MapDocument document, string name, float strength) =>
            Changed(document, name, stack => LayerStackEdits.SetStrength(stack, name, strength, What));

        /// <summary>
        /// The document with a layer working only where a mask is (<see cref="LayerStacks.MaskKey"/>,
        /// <see cref="LayerStacks.StratumKey"/>), or everywhere (null).
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer or mask, or the layer is the base.</exception>
        public static MapDocument SetMask(MapDocument document, string name, string? mask)
        {
            if (mask is not null && LayerStacks.MaskOf(document, mask) is null)
            {
                throw new ArgumentException($"The map has no {LayerStacks.MaskTitle(mask)}", nameof(mask));
            }
            return Changed(document, name, stack => LayerStackEdits.SetClip(stack, name, mask, What));
        }

        /// <summary>
        /// The document with a layer shown or hidden in one stack (the final unless the layer is only
        /// in the draft, or <paramref name="kind"/> names the draft); the other keeps its own choice.
        /// </summary>
        public static MapDocument SetVisible(MapDocument document, string name, bool visible, HeightStackKind kind = HeightStackKind.Final)
        {
            (HeightStackKind found, _) = HeightStacks.Locate(document, name, kind);
            return HeightStacks.SetVisible(document, found, name, visible);
        }

        public static MapDocument SetLocked(MapDocument document, string name, bool locked) =>
            Changed(document, name, stack => LayerStackEdits.SetLocked(stack, name, locked, What));

        /// <summary>
        /// The document with an image layer's values read again from its file.
        /// </summary>
        /// <param name="samples">(width + 1) by (height + 1) samples of the map's height scale.</param>
        /// <exception cref="ArgumentException">There is no such layer, or it is not read from a file.</exception>
        public static MapDocument SetImported(MapDocument document, string name, ushort[] samples, string? source = null)
        {
            (HeightStackKind kind, int index) = HeightStacks.Locate(document, name);
            Layer layer = HeightStacks.Of(document, kind).Layers[index];
            if (layer.Source is not ImageSource image)
            {
                throw new ArgumentException($"The height layer {layer.Name} is not imported", nameof(name));
            }
            TiledBuffer<float> values = FromSamples(TiledBuffer<ushort>.FromArray(document.Width + 1, document.Height + 1, samples), document.Terrain.Heightmap.Scale, layer.Values);
            LayerSource read = source is null || source == image.Path ? image : new ImageSource(source);
            return HeightStacks.WithLayer(document, kind, index, layer with { Values = values, Source = read });
        }

        /// <summary>
        /// The document with the final's base's heights replaced, e.g. by a heightmap another tool
        /// saved over the project's (the draft's too while it shares the base).
        /// </summary>
        /// <param name="samples">(width + 1) by (height + 1) samples.</param>
        public static MapDocument SetBase(MapDocument document, TiledBuffer<ushort> samples)
        {
            document = Start(document);
            Layer layer = document.HeightStack.Layers[0];
            return HeightStacks.WithLayer(document, HeightStackKind.Final, 0, layer with { Values = FromSamples(samples, document.Terrain.Heightmap.Scale, layer.Values) });
        }

        /// <summary>
        /// Ends the stacks: the heightmap keeps what the final's layers make, and the map has no
        /// layers (nor a draft of its own).
        /// </summary>
        public static MapDocument Bake(MapDocument document) =>
            document.HeightStack.IsEmpty && document.DraftStack.IsEmpty
                ? document
                : document with { HeightStack = LayerStack.Empty(LayerTarget.Heightmap), DraftStack = LayerStack.Empty(LayerTarget.Heightmap), DraftSamples = null };

        /// <summary>
        /// The place of a layer in the final's stack, by name (exactly, else without regard to case).
        /// </summary>
        /// <exception cref="ArgumentException">The final has no such layer.</exception>
        public static int IndexOf(MapDocument document, string name) => LayerStackEdits.IndexOf(document.HeightStack, name, What);

        internal static MapDocument With(MapDocument document, int index, Layer layer) =>
            HeightStacks.WithLayer(document, HeightStackKind.Final, index, layer);

        /// <summary>
        /// The document with a layer changed by an edit of its stack (found in the final, else the
        /// draft), the change made in the other stack too.
        /// </summary>
        private static MapDocument Changed(MapDocument document, string name, Func<LayerStack, LayerStack> edit)
        {
            (HeightStackKind kind, int index) = HeightStacks.Locate(document, name);
            LayerStack changed = edit(HeightStacks.Of(document, kind));
            return HeightStacks.WithLayer(document, kind, index, changed.Layers[index]);
        }

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
        private static Layer Converted(MapDocument document, HeightStackKind kind, int index, Layer layer, LayerCombine combine)
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
                    HeightLayers.Heights(document, tileX, tileY, index, below, kind);
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
