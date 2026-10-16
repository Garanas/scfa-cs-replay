using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// How the map's generated results are made (the Procedural mode): which regenerate on their own
    /// after a change to their inputs, which the export leaves alone, and the settings of some. Kept in <c>map.json</c> (<c>procedural</c>); the results themselves live in memory.
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

        /// <summary>
        /// The masks for tools written beside the draft heightmap for World Machine or Gaea
        /// (<see cref="FAForever.MapEditor.Project.ProceduralFiles"/>), by name.
        /// </summary>
        public ImmutableSortedSet<string> DraftMasks { get; init; } = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);

        /// <summary>
        /// Whether the draft heightmap and <see cref="DraftMasks"/> are written again whenever they
        /// change, so the procedural tool always reads the current draft.
        /// </summary>
        public bool WriteDraftOnChange { get; init; }

        /// <summary>
        /// How the path networks are generated, whether the export writes them, and the edits by hand
        /// on top of them.
        /// </summary>
        public PathNodeSettings PathNodes { get; init; } = PathNodeSettings.Default;

        /// <summary>
        /// How the sky cube map is generated from the map.
        /// </summary>
        public SkyCubeSettings SkyCube { get; init; } = SkyCubeSettings.Default;

        public bool IsDefault => AutoGenerate.IsEmpty && NotOnExport.IsEmpty && DraftMasks.IsEmpty && !WriteDraftOnChange && PathNodes.IsDefault && SkyCube.IsDefault;

        public bool RegeneratesOnItsOwn(string id) => AutoGenerate.Contains(id);

        public bool GeneratesOnExport(string id) => !NotOnExport.Contains(id);

        public ProceduralSettings WithAutoGenerate(string id, bool on) =>
            this with { AutoGenerate = on ? AutoGenerate.Add(id) : AutoGenerate.Remove(id) };

        public ProceduralSettings WithOnExport(string id, bool on) =>
            this with { NotOnExport = on ? NotOnExport.Remove(id) : NotOnExport.Add(id) };

        public bool Equals(ProceduralSettings? other) =>
            other is not null && AutoGenerate.SetEquals(other.AutoGenerate) && NotOnExport.SetEquals(other.NotOnExport)
            && DraftMasks.SetEquals(other.DraftMasks) && WriteDraftOnChange == other.WriteDraftOnChange && PathNodes == other.PathNodes && SkyCube == other.SkyCube;

        public override int GetHashCode() => HashCode.Combine(AutoGenerate.Count, NotOnExport.Count, PathNodes);
    }
}
