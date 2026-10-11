using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// A part of the document that a generated result is made from, read from a version of it.
    /// Versions share what they do not change, so an unchanged part is the same object in both and
    /// comparing them is cheap; a part that is a value (an array) compares by what it refers to.
    /// </summary>
    /// <param name="Name">What the person reads when the result is out of date, e.g. <c>heightmap</c>.</param>
    public sealed record GeneratedInput(string Name, Func<MapDocument, object?> Read)
    {
        public static GeneratedInput Heightmap { get; } = new GeneratedInput("heightmap", document => document.Terrain.Heightmap);

        public static GeneratedInput TerrainTypes { get; } = new GeneratedInput("terrain types", document => document.Terrain.Types);

        public static GeneratedInput StratumMasks { get; } = new GeneratedInput("stratum masks", document => document.Terrain.StratumMasks);

        public static GeneratedInput TerrainLayers { get; } = new GeneratedInput("terrain layers", document => document.Terrain.Layers);

        public static GeneratedInput Water { get; } = new GeneratedInput("water", document => document.Water);

        /// <summary>
        /// The height of the water surface, null without water: what walkability reads of the water,
        /// so a change of its colours leaves the result current.
        /// </summary>
        public static GeneratedInput WaterLevel { get; } = new GeneratedInput("water level", document => document.Water.HasWater ? document.Water.Elevation : null);

        /// <summary>
        /// The deposits, the areas and which of them is the playable area of a skirmish map, without
        /// the other markers: what walkability reads of them.
        /// </summary>
        public static GeneratedInput DepositsAndAreas { get; } = new GeneratedInput("deposits and areas", document => (document.Markers.Resources, document.Markers.Areas, document.Scenario.PlayableArea));

        /// <summary>
        /// The map's type (skirmish, co-op): whether its first area is the playable area.
        /// </summary>
        public static GeneratedInput MapType { get; } = new GeneratedInput("map type", document => document.Scenario.Type);

        /// <summary>
        /// Where walkability is marked by hand (user story T-7).
        /// </summary>
        public static GeneratedInput WalkabilityMarks { get; } = new GeneratedInput("walkability marks", document => document.WalkabilityMarks);

        public static GeneratedInput Lighting { get; } = new GeneratedInput("lighting", document => document.Lighting);

        public static GeneratedInput Markers { get; } = new GeneratedInput("markers", document => document.Markers);

        public static GeneratedInput Props { get; } = new GeneratedInput("props", document => document.Props);

        /// <summary>
        /// The map's symmetry, which the props of rules follow.
        /// </summary>
        public static GeneratedInput Symmetry { get; } = new GeneratedInput("symmetry", document => document.Symmetry);

        public static GeneratedInput Decals { get; } = new GeneratedInput("decals", document => document.Decals);

        /// <summary>
        /// The start positions (the spawns), without the other markers.
        /// </summary>
        public static GeneratedInput Spawns { get; } = new GeneratedInput("spawns", document => document.Markers.Spawns);

        /// <summary>
        /// How the path networks are generated (<see cref="PathNodeSettings.Generation"/>), without the
        /// edits by hand and the export's choices.
        /// </summary>
        public static GeneratedInput PathNodeSettings { get; } = new GeneratedInput("path node settings", document => document.Procedural.PathNodes.Generation);

        /// <summary>
        /// Whether two versions of the part are the same.
        /// </summary>
        public static bool Same(object? before, object? now) => Equals(before, now);
    }

    /// <summary>
    /// Something the editor makes from the map when asked (user story F-7): walkability, the normal
    /// map and preview, props or masks from rules. It never runs by itself: the editor tells when
    /// its inputs changed (<see cref="GeneratedResults.Stale"/>) and the person asks for it again.
    /// </summary>
    public interface IGenerator
    {
        /// <summary>
        /// E.g. <c>walkability</c>; the command that generates it is the feature's own.
        /// </summary>
        string Id { get; }

        string Title { get; }

        /// <summary>
        /// The user story it serves, e.g. <c>T-7</c>.
        /// </summary>
        string Story { get; }

        /// <summary>
        /// The parts of the document it reads; a change to another part leaves its result current.
        /// </summary>
        IReadOnlyList<GeneratedInput> Inputs { get; }

        /// <summary>
        /// The parts of the document putting its result in place changes, as inputs of other
        /// generators read them (masks from rules change the masks the prop rules read): a generator
        /// that reads one is generated after it (<see cref="GeneratedResults.InOrder"/>).
        /// </summary>
        IReadOnlyList<GeneratedInput> Outputs => [];

        /// <summary>
        /// The ids of generators whose results it reads besides the document (path networks read
        /// walkability): those are generated before it, and their last result is handed to it
        /// (<see cref="Read"/>) before it generates.
        /// </summary>
        IReadOnlyList<string> Reads => [];

        /// <summary>
        /// The last result of a generator it <see cref="Reads"/>, handed to it before it generates;
        /// it uses the result only while the result is current for the version it generates from.
        /// </summary>
        void Read(GeneratedResult result)
        {
        }

        /// <summary>
        /// Makes the result from a version of the document. It reads only <see cref="Inputs"/> and may
        /// run on a thread of the pool: the document never changes.
        /// </summary>
        object Generate(MapDocument document);

        /// <summary>
        /// <see cref="Generate(MapDocument)"/> that a newer run can stop (a result regenerating on its
        /// own after a change, <see cref="ProceduralSettings.AutoGenerate"/>); a generator that can stop
        /// between its steps overrides it, the others run to the end.
        /// </summary>
        /// <exception cref="OperationCanceledException"><paramref name="cancel"/> was cancelled.</exception>
        object Generate(MapDocument document, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            return Generate(document);
        }

        /// <summary>
        /// The large buffers a result holds, each with its size in bytes, so the results remembered
        /// for undo and redo stay within <see cref="GeneratedResults.RecentBudget"/>. Results that
        /// share a buffer (the navigation mesh holds walkability's grids) name the same object, and
        /// it counts once. A result of small values, or one held by the map itself, names none.
        /// </summary>
        IEnumerable<(object Buffer, long Bytes)> MemoryOf(object value) => [];
    }

    /// <summary>
    /// A generator whose result is a part of the map itself (the normal map, the preview): generating
    /// it is an edit, one step of undo, that puts the result into the document.
    /// </summary>
    public interface IDocumentGenerator : IGenerator
    {
        /// <summary>
        /// The document with <paramref name="value"/>, a result of <see cref="IGenerator.Generate"/>, in its place.
        /// </summary>
        MapDocument Apply(MapDocument document, object value);
    }

    /// <summary>
    /// A result and the versions of the inputs it was made from.
    /// </summary>
    public sealed record GeneratedResult(IGenerator Generator, object Value, ImmutableArray<object?> From)
    {
        /// <summary>
        /// A result made from <paramref name="document"/>; for a result generated elsewhere (on a
        /// thread of the pool) from that version.
        /// </summary>
        public static GeneratedResult Of(IGenerator generator, MapDocument document, object value) =>
            new GeneratedResult(generator, value, [.. generator.Inputs.Select(input => input.Read(document))]);

        /// <summary>
        /// The inputs that differ in <paramref name="document"/> from the version it was made from.
        /// </summary>
        public IReadOnlyList<string> ChangedInputs(MapDocument document) =>
            [.. Generator.Inputs.Where((input, index) => !GeneratedInput.Same(From[index], input.Read(document))).Select(input => input.Name)];

        public bool IsStale(MapDocument document) => ChangedInputs(document).Count > 0;
    }

    /// <summary>
    /// Whether a generator's result is there and current.
    /// </summary>
    public enum GeneratedStatus
    {
        /// <summary>Not generated for this map yet.</summary>
        Missing,

        /// <summary>Generated, and its inputs changed since.</summary>
        Stale,

        /// <summary>Generated from the inputs as they are.</summary>
        Current,
    }

    /// <summary>
    /// How a generator's result stands against a version of the document.
    /// </summary>
    /// <param name="ChangedInputs">The inputs that changed since it was generated; empty unless stale.</param>
    public sealed record GeneratedState(IGenerator Generator, GeneratedStatus Status, GeneratedResult? Result, IReadOnlyList<string> ChangedInputs);

    /// <summary>
    /// The generators of the editor and the last result of each for the open map. A feature registers
    /// its generator once; another map forgets the results.
    /// </summary>
    public sealed class GeneratedResults
    {
        private readonly List<IGenerator> _generators = [];
        private readonly Dictionary<string, GeneratedResult> _results = new Dictionary<string, GeneratedResult>(StringComparer.Ordinal);

        public IReadOnlyList<IGenerator> Generators => _generators;

        /// <exception cref="ArgumentException">A generator with its id is registered already.</exception>
        public void Register(IGenerator generator)
        {
            if (Find(generator.Id) is not null)
            {
                throw new ArgumentException($"A generator '{generator.Id}' is registered already", nameof(generator));
            }
            _generators.Add(generator);
        }

        /// <returns>Whether there was such a generator.</returns>
        public bool Unregister(string id)
        {
            _results.Remove(id);
            if (_recent.Remove(id, out List<GeneratedResult>? recent))
            {
                recent.ForEach(result => _rememberedAt.Remove(result));
            }
            return _generators.RemoveAll(generator => generator.Id == id) > 0;
        }

        public IGenerator? Find(string id) => _generators.FirstOrDefault(generator => generator.Id == id);

        /// <summary>
        /// The last result of a generator, or null.
        /// </summary>
        public GeneratedResult? ResultOf(string id) => _results.GetValueOrDefault(id);

        /// <summary>
        /// Generates a result from <paramref name="document"/> and keeps it.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such generator.</exception>
        public GeneratedResult Generate(string id, MapDocument document)
        {
            IGenerator generator = Find(id) ?? throw new ArgumentException($"There is no generator '{id}'", nameof(id));
            HandResults(generator);
            GeneratedResult result = GeneratedResult.Of(generator, document, generator.Generate(document));
            Keep(result);
            return result;
        }

        /// <summary>
        /// Hands a generator the last results of the generators it <see cref="IGenerator.Reads"/>.
        /// </summary>
        public void HandResults(IGenerator generator)
        {
            foreach (string read in generator.Reads)
            {
                if (ResultOf(read) is GeneratedResult result)
                {
                    generator.Read(result);
                }
            }
        }

        /// <summary>
        /// Whether what <paramref name="first"/> makes is read by <paramref name="then"/>: one of its
        /// outputs is one of the other's inputs (by name), or the other reads its result.
        /// </summary>
        public static bool Feeds(IGenerator first, IGenerator then) =>
            then.Reads.Contains(first.Id, StringComparer.Ordinal)
            || first.Outputs.Any(output => then.Inputs.Any(input => input.Name == output.Name));

        /// <summary>
        /// The generators in an order in which each comes after those that feed it
        /// (<see cref="Feeds"/>), else in the order they were registered; generators that feed each
        /// other (a cycle) keep the order they were registered in.
        /// </summary>
        public IReadOnlyList<IGenerator> InOrder()
        {
            List<IGenerator> ordered = [];
            List<IGenerator> waiting = [.. _generators];
            while (waiting.Count > 0)
            {
                // the first registered that nothing still waiting feeds; in a cycle, the first registered
                IGenerator next = waiting.FirstOrDefault(generator => !waiting.Any(other => other != generator && Feeds(other, generator))) ?? waiting[0];
                ordered.Add(next);
                waiting.Remove(next);
            }
            return ordered;
        }

        /// <summary>
        /// Keeps a result made elsewhere (<see cref="GeneratedResult.Of"/>), replacing the last one,
        /// and remembers it for the version of its inputs (<see cref="Recall"/>).
        /// </summary>
        public void Keep(GeneratedResult result)
        {
            _results[result.Generator.Id] = result;
            Remember(result);
        }

        /// <summary>
        /// Forgets every result: another map is open.
        /// </summary>
        public void Forget()
        {
            _results.Clear();
            _recent.Clear();
            _rememberedAt.Clear();
        }

        // the last results of each generator, the newest last, and when each was remembered
        private readonly Dictionary<string, List<GeneratedResult>> _recent = new Dictionary<string, List<GeneratedResult>>(StringComparer.Ordinal);
        private readonly Dictionary<GeneratedResult, long> _rememberedAt = new Dictionary<GeneratedResult, long>(ReferenceEqualityComparer.Instance);
        private long _remembered;

        /// <summary>
        /// How many results of each generator are remembered for undo and redo: 8.
        /// </summary>
        public const int RecentPerGenerator = 8;

        /// <summary>
        /// How many bytes of large buffers (<see cref="IGenerator.MemoryOf"/>) the remembered results
        /// may hold together; the ones remembered first go first, and a generator's newest stays.
        /// 1 GiB keeps about four versions of walkability with its connected regions on a map of 81 km.
        /// </summary>
        public long RecentBudget { get; set; } = 1L << 30;

        /// <summary>
        /// The results remembered for a generator, the oldest first (automation and tests).
        /// </summary>
        public IReadOnlyList<GeneratedResult> RecentOf(string id) => _recent.GetValueOrDefault(id) ?? [];

        /// <summary>
        /// Remembers a result for the version of its inputs, so an undo or redo back to that version
        /// keeps it again (<see cref="Recall"/>) and nothing regenerates. <see cref="Keep"/> does this.
        /// </summary>
        public void Remember(GeneratedResult result)
        {
            if (Find(result.Generator.Id) is null)
            {
                return;
            }
            if (!_recent.TryGetValue(result.Generator.Id, out List<GeneratedResult>? recent))
            {
                _recent[result.Generator.Id] = recent = [];
            }
            if (recent.Any(kept => ReferenceEquals(kept, result)))
            {
                return;
            }
            recent.Add(result);
            _rememberedAt[result] = ++_remembered;
            if (recent.Count > RecentPerGenerator)
            {
                _rememberedAt.Remove(recent[0]);
                recent.RemoveAt(0);
            }
            KeepWithinBudget();
        }

        // drops the results remembered first until their distinct buffers fit the budget
        private void KeepWithinBudget()
        {
            while (MemoryOfRecent() > RecentBudget)
            {
                GeneratedResult? oldest = _recent.Values
                    .Where(list => list.Count > 1)
                    .Select(list => list[0])
                    .MinBy(result => _rememberedAt[result]);
                if (oldest is null)
                {
                    return;
                }
                _recent[oldest.Generator.Id].RemoveAt(0);
                _rememberedAt.Remove(oldest);
            }
        }

        /// <summary>
        /// The bytes of the distinct large buffers the remembered results hold.
        /// </summary>
        public long MemoryOfRecent()
        {
            HashSet<object> counted = new HashSet<object>(ReferenceEqualityComparer.Instance);
            long bytes = 0;
            foreach (GeneratedResult result in _recent.Values.SelectMany(list => list))
            {
                foreach ((object buffer, long size) in result.Generator.MemoryOf(result.Value))
                {
                    if (counted.Add(buffer))
                    {
                        bytes += size;
                    }
                }
            }
            return bytes;
        }

        /// <summary>
        /// After an undo or redo: keeps again, for each generator whose last result is missing or out
        /// of date in <paramref name="document"/>, the newest remembered result made from its inputs as
        /// they are there (each version keeps the results made for it).
        /// </summary>
        /// <returns>The generators whose result was kept again.</returns>
        public IReadOnlyList<IGenerator> Recall(MapDocument document)
        {
            List<IGenerator> recalled = [];
            foreach (IGenerator generator in _generators)
            {
                if (ResultOf(generator.Id) is GeneratedResult now && !now.IsStale(document))
                {
                    continue;
                }
                if (_recent.GetValueOrDefault(generator.Id)?.LastOrDefault(result => !result.IsStale(document)) is GeneratedResult earlier)
                {
                    _results[generator.Id] = earlier;
                    recalled.Add(generator);
                }
            }
            return recalled;
        }

        /// <summary>
        /// How every generator's result stands against <paramref name="document"/>, in the order they
        /// were registered.
        /// </summary>
        public IReadOnlyList<GeneratedState> States(MapDocument document) => [.. _generators.Select(generator =>
        {
            if (ResultOf(generator.Id) is not GeneratedResult result)
            {
                return new GeneratedState(generator, GeneratedStatus.Missing, null, []);
            }
            IReadOnlyList<string> changed = result.ChangedInputs(document);
            return new GeneratedState(generator, changed.Count > 0 ? GeneratedStatus.Stale : GeneratedStatus.Current, result, changed);
        })];

        /// <summary>
        /// The results whose inputs changed since they were generated, in the order to generate them
        /// again (<see cref="InOrder"/>).
        /// </summary>
        public IReadOnlyList<GeneratedState> Stale(MapDocument document)
        {
            IReadOnlyList<GeneratedState> states = States(document);
            return [.. InOrder()
                .Select(generator => states.First(state => state.Generator == generator))
                .Where(state => state.Status == GeneratedStatus.Stale)];
        }
    }
}
