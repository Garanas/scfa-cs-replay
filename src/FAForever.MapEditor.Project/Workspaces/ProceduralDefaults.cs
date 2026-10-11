using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// What a workspace gives the maps made or imported in it (the Procedural mode's settings that
    /// are the map maker's habit rather than the map's): which generated results regenerate on their
    /// own, and which the export makes or writes (<c>workspace.json</c>'s <c>procedural</c>). A project
    /// opened keeps its own settings.
    /// </summary>
    public sealed record ProceduralDefaults
    {
        /// <summary>
        /// The results with a choice on export: the normal map (made again when out of date) and the
        /// path networks (written into the markers).
        /// </summary>
        public static IReadOnlyList<string> ExportChoices { get; } = [NormalMapGenerator.GeneratorId, PathNetworkGenerator.GeneratorId];

        /// <summary>
        /// Without a <c>procedural</c> block: walkability, the navigation mesh (connected regions) and
        /// the masks from rules regenerate on their own, the path networks not (each while the views
        /// show it); the export makes the normal map again when out of date and writes no path networks.
        /// </summary>
        public static ProceduralDefaults Default { get; } = new ProceduralDefaults();

        /// <summary>
        /// The ids of the generators that regenerate on their own.
        /// </summary>
        public ImmutableSortedSet<string> AutoGenerate { get; init; } =
            ImmutableSortedSet.Create(StringComparer.Ordinal, WalkabilityGenerator.GeneratorId, NavigationMeshGenerator.GeneratorId, MaskRulesGenerator.GeneratorId);

        /// <summary>
        /// The ids among <see cref="ExportChoices"/> that the export makes or writes.
        /// </summary>
        public ImmutableSortedSet<string> OnExport { get; init; } = ImmutableSortedSet.Create(StringComparer.Ordinal, NormalMapGenerator.GeneratorId);

        public bool IsDefault => Equals(Default);

        public bool RegeneratesOnItsOwn(string id) => AutoGenerate.Contains(id);

        public ProceduralDefaults WithAutoGenerate(string id, bool on) =>
            this with { AutoGenerate = on ? AutoGenerate.Add(id) : AutoGenerate.Remove(id) };

        public ProceduralDefaults WithOnExport(string id, bool on) =>
            this with { OnExport = on ? OnExport.Add(id) : OnExport.Remove(id) };

        /// <summary>
        /// A map's settings with these defaults: what regenerates on its own, and the export's choices
        /// (the path networks' choice is set only when on, so a map that never chose keeps "the first
        /// Generate turns it on"). The rest of the settings stay.
        /// </summary>
        public ProceduralSettings ApplyTo(ProceduralSettings settings) => settings with
        {
            AutoGenerate = AutoGenerate,
            NotOnExport = OnExport.Contains(NormalMapGenerator.GeneratorId)
                ? settings.NotOnExport.Remove(NormalMapGenerator.GeneratorId)
                : settings.NotOnExport.Add(NormalMapGenerator.GeneratorId),
            PathNodes = OnExport.Contains(PathNetworkGenerator.GeneratorId)
                ? settings.PathNodes with { OnExport = true }
                : settings.PathNodes.OnExport is null ? settings.PathNodes : settings.PathNodes with { OnExport = false },
        };

        /// <summary>
        /// A map's settings as defaults ("Use this map's settings as the workspace default").
        /// </summary>
        public static ProceduralDefaults Of(ProceduralSettings settings) => new ProceduralDefaults
        {
            AutoGenerate = settings.AutoGenerate,
            OnExport = ImmutableSortedSet.CreateRange(StringComparer.Ordinal,
                ExportChoices.Where(id => id == PathNetworkGenerator.GeneratorId ? settings.PathNodes.WritesOnExport : settings.GeneratesOnExport(id))),
        };

        /// <summary>
        /// These defaults with a map's choices for one result: whether it regenerates on its own and,
        /// when the export has a choice about it, whether the export makes or writes it ("Use as
        /// workspace default" on that result's entry). The other results keep their defaults.
        /// </summary>
        public ProceduralDefaults WithChoicesOf(ProceduralSettings settings, string id)
        {
            ProceduralDefaults map = Of(settings);
            ProceduralDefaults defaults = WithAutoGenerate(id, map.RegeneratesOnItsOwn(id));
            return ExportChoices.Contains(id) ? defaults.WithOnExport(id, map.OnExport.Contains(id)) : defaults;
        }

        /// <summary>
        /// Whether a map makes the same choices for one result as these defaults (<see cref="WithChoicesOf"/>).
        /// </summary>
        public bool HasChoicesOf(ProceduralSettings settings, string id) => Equals(WithChoicesOf(settings, id));

        public bool Equals(ProceduralDefaults? other) =>
            other is not null && AutoGenerate.SetEquals(other.AutoGenerate) && OnExport.SetEquals(other.OnExport);

        public override int GetHashCode() => HashCode.Combine(AutoGenerate.Count, OnExport.Count);
    }
}
