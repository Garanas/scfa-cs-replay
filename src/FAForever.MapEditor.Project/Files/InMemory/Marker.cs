using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A marker: a named place of the map that the game, the AI or a script reads. Each kind of
    /// marker derives from it and fixes its colour; the export writes the rest of what the map
    /// editors write for it (<see cref="MarkerTranslation"/>).
    /// </summary>
    public abstract record Marker
    {
        /// <summary>
        /// Tells the marker apart while the map is being edited; links between markers (path nodes,
        /// chains) use it. A project does not store it: on disk markers name each other.
        /// </summary>
        public required int Id { get; init; }

        /// <summary>
        /// The marker's name in the game, which scripts use, e.g. <c>Mass 08</c> or <c>ARMY_1</c>.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Where the marker is, in game units (x and z across the map, y the height).
        /// </summary>
        public Vector3 Position { get; init; }

        /// <summary>
        /// The rotation around each axis, in radians.
        /// </summary>
        public Vector3 Orientation { get; init; }

        /// <summary>
        /// The colour the map editors show the marker in.
        /// </summary>
        public abstract Color32 Color { get; }
    }
}
