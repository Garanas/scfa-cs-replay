using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The version of the <c>.scmap</c> a map is written as (<see cref="Terrain.ScmapVersion"/>).
    /// 56 and 60 differ in the skybox, which version 60 stores and the game draws, the imager
    /// elevation offset after the minimap's colours and the count of the strategic view's decals after
    /// the skybox (the engine's gates are 58, 57 and 59); 53 holds six terrain layers and one stratum
    /// mask, so a map of 53 keeps its version.
    /// </summary>
    public static class ScmapVersions
    {
        /// <summary>
        /// The versions a map can be switched between.
        /// </summary>
        public static IReadOnlyList<int> Choices { get; } = [60, 56];

        /// <summary>
        /// The document written as another version: to 60 it gets the skybox the game gave it
        /// (<see cref="Skyboxes.GameDefault"/>) when it has none, so it looks as before; to 56 its
        /// skybox stays in the project, unused by the game.
        /// </summary>
        /// <exception cref="ArgumentException">The version is none of <see cref="Choices"/>.</exception>
        /// <exception cref="InvalidOperationException">The map is of version 53.</exception>
        public static MapDocument Set(MapDocument document, int version)
        {
            if (!Choices.Contains(version))
            {
                throw new ArgumentException($"A map is written as version {string.Join(" or ", Choices)}, not {version}");
            }
            if (document.Terrain.ScmapVersion < NewMap.TenLayersFrom)
            {
                throw new InvalidOperationException($"A version {document.Terrain.ScmapVersion} map holds {document.Terrain.Layers.Length} terrain layers and keeps its version");
            }
            MapDocument changed = document.Terrain.ScmapVersion == version
                ? document
                : document with { Terrain = document.Terrain with { ScmapVersion = version } };
            return version >= 60 ? Skyboxes.WithGameDefault(changed) : changed;
        }
    }
}
