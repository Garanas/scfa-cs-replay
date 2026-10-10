using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The map's two heightmaps: the draft (<see cref="MapDocument.DraftStack"/>), the rough shape
    /// blocked out and the input of World Machine or Gaea, and the final
    /// (<see cref="MapDocument.HeightStack"/>), which the game gets. Each is a stack of layers of its
    /// own; a layer can be in one or in both (<see cref="LayerUse"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A shared layer is the same name in both stacks with the same source, grids and settings, at a
    /// place of its own in each and shown or hidden in each on its own (<see cref="Layer.Visible"/>).
    /// An edit of its content in one stack is made in the other too (<see cref="WithLayer"/>, and
    /// <see cref="Sync"/> after every step for what brushes write). Names are unique over both stacks,
    /// so a name always means one layer.
    /// </para>
    /// <para>
    /// Both stacks are empty, or both have a base (their lowest layer). A map's stacks begin with every
    /// layer shared, the base too, so the draft and the final make the same terrain until they part:
    /// the final's base becomes the procedural tool's output (<see cref="UseAsFinalBase"/>), an image
    /// layer read again whenever its file changes, while the old base stays the draft's.
    /// </para>
    /// </remarks>
    public static class HeightStacks
    {
        /// <summary>
        /// The name the final's base gets when it is read from a procedural tool's output.
        /// </summary>
        public const string FinalBaseName = "Procedural base";

        /// <summary>
        /// One of the two stacks.
        /// </summary>
        public static LayerStack Of(MapDocument document, HeightStackKind kind) =>
            kind == HeightStackKind.Draft ? document.DraftStack : document.HeightStack;

        /// <summary>
        /// The heightmap one of the stacks makes: the terrain's (the final) or the draft's.
        /// </summary>
        public static Heightmap HeightmapOf(MapDocument document, HeightStackKind kind) =>
            kind == HeightStackKind.Draft ? document.DraftHeightmap : document.Terrain.Heightmap;

        /// <summary>
        /// The document with one of the stacks replaced.
        /// </summary>
        public static MapDocument With(MapDocument document, HeightStackKind kind, LayerStack stack)
        {
            if (ReferenceEquals(Of(document, kind), stack))
            {
                return document;
            }
            return kind == HeightStackKind.Draft ? document with { DraftStack = stack } : document with { HeightStack = stack };
        }

        public static HeightStackKind Other(HeightStackKind kind) => kind == HeightStackKind.Draft ? HeightStackKind.Final : HeightStackKind.Draft;

        /// <summary>
        /// Every layer of the map once: the final's in their order, then the draft's that only the
        /// draft has.
        /// </summary>
        public static IEnumerable<Layer> AllLayers(MapDocument document) =>
            document.HeightStack.Layers.Concat(document.DraftStack.Layers.Where(layer => document.HeightStack.Find(layer.Name) is null));

        /// <summary>
        /// The layer of that name in either stack (the final's version of a shared one), or null.
        /// </summary>
        public static Layer? Find(MapDocument document, string? name) => document.HeightStack.Find(name) ?? document.DraftStack.Find(name);

        /// <summary>
        /// Which stacks a layer is in; null when neither has it.
        /// </summary>
        public static LayerUse? UseOf(MapDocument document, string name)
        {
            bool final = document.HeightStack.Find(name) is not null;
            bool draft = document.DraftStack.Find(name) is not null;
            return (final, draft) switch
            {
                (true, true) => LayerUse.Shared,
                (true, false) => LayerUse.Final,
                (false, true) => LayerUse.Draft,
                _ => null,
            };
        }

        /// <summary>
        /// Whether a use puts a layer in a stack.
        /// </summary>
        public static bool IsIn(LayerUse use, HeightStackKind kind) =>
            use == LayerUse.Shared || (use == LayerUse.Draft) == (kind == HeightStackKind.Draft);

        /// <summary>
        /// Whether the final's base is the draft's (shared): the final has no base of its own yet.
        /// </summary>
        public static bool SharesBase(MapDocument document) =>
            document.HeightStack.IsEmpty
            || (document.DraftStack.Layers.FirstOrDefault() is Layer draft && document.HeightStack.Layers[0].Name == draft.Name);

        /// <summary>
        /// The final's own base when it has one (the procedural tool's output), else null.
        /// </summary>
        public static Layer? FinalBase(MapDocument document) => SharesBase(document) ? null : document.HeightStack.Layers.FirstOrDefault();

        /// <summary>
        /// Where a new layer goes when added while a stack is shown: into both while the final shares
        /// the draft's base (a map without a procedural step exports what was drawn), else into the
        /// shown stack only.
        /// </summary>
        public static LayerUse DefaultUse(MapDocument document, HeightStackKind shown) =>
            SharesBase(document) ? LayerUse.Shared : shown == HeightStackKind.Draft ? LayerUse.Draft : LayerUse.Final;

        /// <summary>
        /// Why a name cannot be a new height layer's name, or null when it can: unique over both
        /// stacks, without regard to case.
        /// </summary>
        /// <param name="except">A layer the name may equal (one being renamed).</param>
        public static string? NameProblem(MapDocument document, string name, string? except = null, string what = "height layer") =>
            LayerStackEdits.NameProblem(document.HeightStack, name, except, what)
            ?? LayerStackEdits.NameProblem(document.DraftStack, name, except, what);

        /// <summary>
        /// A name no layer of either stack has: <paramref name="stem"/>, else with the first free number after it.
        /// </summary>
        public static string FreeName(MapDocument document, string stem)
        {
            if (NameProblem(document, stem) is null)
            {
                return stem;
            }
            for (int number = 2; ; number++)
            {
                string name = $"{stem} {number}";
                if (NameProblem(document, name) is null)
                {
                    return name;
                }
            }
        }

        /// <summary>
        /// Where a layer is: the stack <paramref name="prefer"/> when it has it, else the other.
        /// </summary>
        /// <exception cref="ArgumentException">Neither stack has it.</exception>
        public static (HeightStackKind Kind, int Index) Locate(MapDocument document, string name, HeightStackKind prefer = HeightStackKind.Final)
        {
            int index = Of(document, prefer).IndexOf(name);
            if (index >= 0)
            {
                return (prefer, index);
            }
            HeightStackKind other = Other(prefer);
            index = Of(document, other).IndexOf(name);
            if (index >= 0)
            {
                return (other, index);
            }
            List<string> names = [.. AllLayers(document).Select(layer => layer.Name)];
            throw new ArgumentException(names.Count == 0
                ? "The map has no height layers"
                : $"The map has no height layer '{name}': {string.Join(", ", names)}", nameof(name));
        }

        /// <summary>
        /// The document with the layer at <paramref name="index"/> of a stack replaced, and the same
        /// layer in the other stack too (keeping its own <see cref="Layer.Visible"/>): a shared layer's
        /// content is one. A rename is followed in the other stack.
        /// </summary>
        public static MapDocument WithLayer(MapDocument document, HeightStackKind kind, int index, Layer layer)
        {
            LayerStack stack = Of(document, kind);
            Layer was = stack.Layers[index];
            document = With(document, kind, stack.With(index, layer));
            LayerStack other = Of(document, Other(kind));
            int there = other.IndexOf(was.Name);
            if (there >= 0 && other.Layers[there].Name == was.Name)
            {
                Layer theirs = other.Layers[there];
                Layer copy = layer.Visible == theirs.Visible ? layer : layer with { Visible = theirs.Visible };
                if (!ReferenceEquals(copy, theirs) && (copy != theirs || ReferenceEquals(copy, layer)))
                {
                    document = With(document, Other(kind), other.With(there, copy));
                }
            }
            return document;
        }

        /// <summary>
        /// The document with a layer's place in one stack changed (0 the bottom, 1 just above the base).
        /// </summary>
        /// <exception cref="ArgumentException">The stack has no such layer, it is the base, or the place is not above the base.</exception>
        public static MapDocument Move(MapDocument document, HeightStackKind kind, string name, int index) =>
            With(document, kind, LayerStackEdits.Move(Of(document, kind), name, index, "height layer"));

        /// <summary>
        /// The document with a layer shown or hidden in one stack; the other stack keeps its own choice.
        /// </summary>
        /// <exception cref="ArgumentException">The stack has no such layer.</exception>
        public static MapDocument SetVisible(MapDocument document, HeightStackKind kind, string name, bool visible) =>
            With(document, kind, LayerStackEdits.SetVisible(Of(document, kind), name, visible, "height layer"));

        /// <summary>
        /// The document with a layer in the stacks a use names: added to a stack that lacks it (on
        /// top, shown), taken out of one that should not have it. A base keeps its use.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such layer, or it is a base.</exception>
        public static MapDocument SetUse(MapDocument document, string name, LayerUse use)
        {
            (HeightStackKind kind, int index) = Locate(document, name);
            Layer layer = Of(document, kind).Layers[index];
            if (UseOf(document, name) == use)
            {
                return document;
            }
            if (layer.IsBase)
            {
                throw new ArgumentException($"The base {layer.Name} stays where it is: the final gets a base of its own with \"Use as the final's base\"", nameof(name));
            }
            foreach (HeightStackKind each in new[] { HeightStackKind.Final, HeightStackKind.Draft })
            {
                LayerStack stack = Of(document, each);
                int at = stack.IndexOf(name);
                if (IsIn(use, each) && at < 0)
                {
                    document = With(document, each, stack with { Layers = stack.Layers.Add(layer with { Visible = true }) });
                }
                else if (!IsIn(use, each) && at >= 0)
                {
                    document = With(document, each, stack with { Layers = stack.Layers.RemoveAt(at) });
                }
            }
            return document;
        }

        /// <summary>
        /// The document with the final's base read from a file (the procedural tool's output): an
        /// image layer marked base, read again whenever the file changes. The base the final shared
        /// with the draft stays the draft's; a base of the final's own is pointed at the new file.
        /// </summary>
        /// <param name="samples">(width + 1) by (height + 1) samples of the map's height scale.</param>
        /// <param name="source">The file, relative to the project folder when it lies in it.</param>
        public static MapDocument UseAsFinalBase(MapDocument document, ushort[] samples, string source, string? name = null)
        {
            document = HeightLayerEdits.Start(document);
            TiledBuffer<float> values = HeightLayerEdits.FromSamples(TiledBuffer<ushort>.FromArray(document.Width + 1, document.Height + 1, samples), document.Terrain.Heightmap.Scale,
                FinalBase(document)?.Values);
            LayerStack final = document.HeightStack;
            if (FinalBase(document) is Layer own)
            {
                Layer read = own with { Source = new ImageSource(source), Values = values, Coverage = null };
                return With(document, HeightStackKind.Final, final.With(0, read));
            }
            string chosen = name?.Trim() is { Length: > 0 } given ? given : FreeName(document, FinalBaseName);
            if (NameProblem(document, chosen) is string problem)
            {
                throw new ArgumentException(problem, nameof(name));
            }
            Layer layer = new Layer
            {
                Name = chosen,
                Source = new ImageSource(source),
                Combine = LayerCombine.Replace,
                IsBase = true,
                Values = values,
            };
            return With(document, HeightStackKind.Final, final with { Layers = final.Layers.SetItem(0, layer) });
        }

        /// <summary>
        /// The document whose final shares the draft's base again: the final's own base (the
        /// procedural output) is gone.
        /// </summary>
        public static MapDocument ShareDraftBase(MapDocument document)
        {
            if (SharesBase(document))
            {
                return document;
            }
            Layer base_ = document.DraftStack.Layers[0];
            return With(document, HeightStackKind.Final, document.HeightStack with { Layers = document.HeightStack.Layers.SetItem(0, base_ with { Visible = true }) });
        }

        /// <summary>
        /// The stacks as they must be: a draft for a final that has layers (an older project, a stack
        /// just begun: every layer shared), none for a final without.
        /// </summary>
        public static MapDocument Normalized(MapDocument document)
        {
            if (document.HeightStack.IsEmpty)
            {
                return document.DraftStack.IsEmpty && document.DraftSamples is null
                    ? document
                    : document with { DraftStack = LayerStack.Empty(LayerTarget.Heightmap), DraftSamples = null };
            }
            if (document.DraftStack.IsEmpty)
            {
                return document with { DraftStack = document.HeightStack, DraftSamples = null };
            }
            return document;
        }

        /// <summary>
        /// Whether the two stacks make the same heights for sure: the same layers in the same order.
        /// </summary>
        public static bool MakeTheSame(LayerStack final, LayerStack draft) =>
            ReferenceEquals(final, draft) || final.Layers.AsSpan().SequenceEqual(draft.Layers.AsSpan(), ReferenceEqualityComparer.Instance);

        /// <summary>
        /// The layers changed in one stack between two versions carried into the other, unless the
        /// other changed them itself: what a brush wrote into a shared layer of one is in both.
        /// </summary>
        public static MapDocument Sync(MapDocument previous, MapDocument next)
        {
            LayerStack final = next.HeightStack;
            LayerStack draft = next.DraftStack;
            if (draft.IsEmpty || final.IsEmpty)
            {
                return next;
            }
            LayerStack beforeFinal = previous.HeightStack;
            LayerStack beforeDraft = previous.DraftStack.IsEmpty ? previous.HeightStack : previous.DraftStack;
            bool finalChanged = !ReferenceEquals(final, beforeFinal);
            bool draftChanged = !ReferenceEquals(draft, beforeDraft);
            if (!finalChanged && !draftChanged)
            {
                return next;
            }
            LayerStack syncedDraft = finalChanged ? Carry(beforeFinal, final, beforeDraft, draft) : draft;
            LayerStack syncedFinal = draftChanged ? Carry(beforeDraft, draft, beforeFinal, final) : final;
            if (!ReferenceEquals(syncedDraft, draft))
            {
                next = next with { DraftStack = syncedDraft };
            }
            if (!ReferenceEquals(syncedFinal, final))
            {
                next = next with { HeightStack = syncedFinal };
            }
            return next;
        }

        private static LayerStack Carry(LayerStack fromBefore, LayerStack from, LayerStack toBefore, LayerStack to)
        {
            ImmutableArray<Layer> layers = to.Layers;
            foreach (Layer layer in from.Layers)
            {
                if (fromBefore.Layers.Contains(layer, ReferenceEqualityComparer.Instance))
                {
                    continue;
                }
                int index = IndexExactly(layers, layer.Name);
                if (index < 0 || IndexExactly(fromBefore.Layers, layer.Name) < 0)
                {
                    continue;
                }
                Layer theirs = layers[index];
                if (!toBefore.Layers.Contains(theirs, ReferenceEqualityComparer.Instance))
                {
                    // the other stack's layer changed in the same step: the edit made both
                    continue;
                }
                Layer copy = layer.Visible == theirs.Visible ? layer : layer with { Visible = theirs.Visible };
                if (!ReferenceEquals(copy, theirs) && (copy != theirs || ReferenceEquals(copy, layer)))
                {
                    layers = layers.SetItem(index, copy);
                }
            }
            return layers == to.Layers ? to : to with { Layers = layers };
        }

        private static int IndexExactly(ImmutableArray<Layer> layers, string name)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].Name == name)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
