using FAForever.FileFormats.Map;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The symmetry the map is made by (user story F-12), which the editor mirrors edits by: kept with
    /// the project so that the map opens with it, and never exported (the game has no symmetry).
    /// </summary>
    /// <param name="Count">The players of a rotational symmetry or the turns of a kaleidoscope, 2 to
    /// <see cref="MapSymmetry.MaximumCount"/>; 2 for the other kinds.</param>
    public sealed record SymmetrySetting(MapSymmetryKind Kind, int Count)
    {
        /// <summary>
        /// The rectangle the symmetry maps onto itself, in ogrids: the playable area as it was when the
        /// symmetry was put around it (it stays where it is when the area changes); null for the whole map.
        /// </summary>
        public MapSymmetryArea? Around { get; init; }

        /// <summary>
        /// The rectangle on a map of that size: <see cref="Around"/>, else the whole map.
        /// </summary>
        public MapSymmetryArea AreaOn(float width, float height) => Around ?? MapSymmetryArea.Whole(width, height);

        /// <summary>
        /// The parts of a map of that size (<see cref="MapSymmetry.Transforms(MapSymmetryKind, int, MapSymmetryArea)"/>), the identity first.
        /// </summary>
        public IReadOnlyList<MapSymmetryTransform> Transforms(float width, float height) => MapSymmetry.Transforms(Kind, Count, AreaOn(width, height));

        /// <summary>
        /// The parts of the document's map, the identity first.
        /// </summary>
        public IReadOnlyList<MapSymmetryTransform> Transforms(MapDocument document) => Transforms(document.Width, document.Height);

        /// <summary>
        /// The lines between the parts on a map of that size (<see cref="MapSymmetry.Guides(MapSymmetryKind, int, MapSymmetryArea)"/>).
        /// </summary>
        public (System.Numerics.Vector2 From, System.Numerics.Vector2 To)[] Guides(float width, float height) => MapSymmetry.Guides(Kind, Count, AreaOn(width, height));

        /// <summary>
        /// The point the point symmetry turns about on a map of that size (<see cref="MapSymmetry.Point(MapSymmetryKind, int, MapSymmetryArea)"/>);
        /// null for the kinds whose guides are lines.
        /// </summary>
        public System.Numerics.Vector2? Point(float width, float height) => MapSymmetry.Point(Kind, Count, AreaOn(width, height));

        /// <summary>
        /// The playable area of the document as a symmetry's rectangle; null for a map without areas,
        /// which is playable as a whole.
        /// </summary>
        public static MapSymmetryArea? PlayableAreaOf(MapDocument document) => document.PlayableArea is Area area
            ? new MapSymmetryArea((float)Math.Min(area.X0, area.X1), (float)Math.Min(area.Z0, area.Z1), (float)Math.Max(area.X0, area.X1), (float)Math.Max(area.Z0, area.Z1))
            : null;
    }
}
