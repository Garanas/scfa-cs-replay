using FAForever.FileFormats.Map;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// The symmetry the map is made by (user story F-12), which the editor mirrors edits by: kept with
    /// the project so that the map opens with it, and never exported (the game has no symmetry).
    /// </summary>
    /// <param name="Count">The players of a rotational symmetry, 2 to <see cref="MapSymmetry.MaximumCount"/>; 2 for a mirror.</param>
    public sealed record SymmetrySetting(MapSymmetryKind Kind, int Count);
}
