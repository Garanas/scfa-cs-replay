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
        /// The deposits and the areas (the first is the playable area of a skirmish map), without the
        /// other markers: what walkability reads of them.
        /// </summary>
        public static GeneratedInput DepositsAndAreas { get; } = new GeneratedInput("deposits and areas", document => (document.Markers.Resources, document.Markers.Areas));

        /// <summary>
        /// The map's type (skirmish, co-op): whether its first area is the playable area.
        /// </summary>
        public static GeneratedInput MapType { get; } = new GeneratedInput("map type", document => document.Scenario.Type);

        public static GeneratedInput Lighting { get; } = new GeneratedInput("lighting", document => document.Lighting);

        public static GeneratedInput Markers { get; } = new GeneratedInput("markers", document => document.Markers);

        public static GeneratedInput Props { get; } = new GeneratedInput("props", document => document.Props);

        public static GeneratedInput Decals { get; } = new GeneratedInput("decals", document => document.Decals);

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
        /// Makes the result from a version of the document. It reads only <see cref="Inputs"/> and may
        /// run on a thread of the pool: the document never changes.
        /// </summary>
        object Generate(MapDocument document);
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
            GeneratedResult result = GeneratedResult.Of(generator, document, generator.Generate(document));
            Keep(result);
            return result;
        }

        /// <summary>
        /// Keeps a result made elsewhere (<see cref="GeneratedResult.Of"/>), replacing the last one.
        /// </summary>
        public void Keep(GeneratedResult result) => _results[result.Generator.Id] = result;

        /// <summary>
        /// Forgets every result: another map is open.
        /// </summary>
        public void Forget() => _results.Clear();

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
        /// The results whose inputs changed since they were generated.
        /// </summary>
        public IReadOnlyList<GeneratedState> Stale(MapDocument document) => [.. States(document).Where(state => state.Status == GeneratedStatus.Stale)];
    }
}
