using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// How the map's generated results are made (the Procedural mode): which regenerate on their own
    /// after a change to their inputs, which the export leaves alone, and the navigation mesh's
    /// settings. Kept in <c>map.json</c> (<c>procedural</c>); the results themselves live in memory.
    /// </summary>
    public sealed record ProceduralSettings
    {
        public static ProceduralSettings Default { get; } = new ProceduralSettings();

        /// <summary>
        /// The ids of the generators that regenerate on their own, a moment after the last change to
        /// their inputs (<c>walkability</c>).
        /// </summary>
        public ImmutableSortedSet<string> AutoGenerate { get; init; } = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);

        /// <summary>
        /// The ids of the generators the export does not make again when they are out of date (the
        /// normal map, when the map maker keeps one of their own); every other is made.
        /// </summary>
        public ImmutableSortedSet<string> NotOnExport { get; init; } = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);

        public NavigationSettings Navigation { get; init; } = NavigationSettings.Default;

        /// <summary>
        /// How the path networks are generated, whether the export writes them, and the edits by hand
        /// on top of them.
        /// </summary>
        public PathNodeSettings PathNodes { get; init; } = PathNodeSettings.Default;

        public bool IsDefault => AutoGenerate.IsEmpty && NotOnExport.IsEmpty && Navigation == NavigationSettings.Default && PathNodes.IsDefault;

        public bool RegeneratesOnItsOwn(string id) => AutoGenerate.Contains(id);

        public bool GeneratesOnExport(string id) => !NotOnExport.Contains(id);

        public ProceduralSettings WithAutoGenerate(string id, bool on) =>
            this with { AutoGenerate = on ? AutoGenerate.Add(id) : AutoGenerate.Remove(id) };

        public ProceduralSettings WithOnExport(string id, bool on) =>
            this with { NotOnExport = on ? NotOnExport.Remove(id) : NotOnExport.Add(id) };

        public bool Equals(ProceduralSettings? other) =>
            other is not null && AutoGenerate.SetEquals(other.AutoGenerate) && NotOnExport.SetEquals(other.NotOnExport) && Navigation == other.Navigation && PathNodes == other.PathNodes;

        public override int GetHashCode() => HashCode.Combine(AutoGenerate.Count, NotOnExport.Count, Navigation, PathNodes);
    }

    /// <summary>
    /// Which starts the navigation mesh measures routes between.
    /// </summary>
    public enum RoutePairs
    {
        /// <summary>No routes.</summary>
        None,

        /// <summary>Each start to the start nearest to it in a straight line.</summary>
        Nearest,

        /// <summary>Every pair of starts.</summary>
        All,
    }

    /// <summary>
    /// The navigation mesh's settings (the Procedural mode): the routes it measures.
    /// </summary>
    public sealed record NavigationSettings
    {
        public static NavigationSettings Default { get; } = new NavigationSettings();

        public RoutePairs Routes { get; init; } = RoutePairs.Nearest;
    }
}
