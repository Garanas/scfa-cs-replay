using System.Numerics;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Moves that keep structures on the build grid (user story U-11): the select tool's drag, the arrow
/// keys and <c>edit.place</c> ask here for the offset to use.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The offset of a move: the one given, or while <see cref="HandleSettings.KeepOnGrid"/> is on
    /// and the things include a structure, rounded so that the first structure lands on the build
    /// grid (<see cref="BuildGrid.KeepOnGrid"/>); every thing moves by it.
    /// </summary>
    public Vector2 KeepOnGrid(MapDocument document, IEnumerable<ObjectRef> things, Vector2 offset)
    {
        if (!Handles.KeepOnGrid)
        {
            return offset;
        }
        using Performance.Span measure = Performance.Measure("tool.keep-on-grid");
        (float dx, float dz) = BuildGrid.KeepOnGrid(document, things, offset.X, offset.Y, UnitKinds.StructureFootprint(Assets.Catalog));
        return new Vector2(dx, dz);
    }

    /// <summary>
    /// The place <c>edit.place</c> puts things at, rounded like a move's offset
    /// (<see cref="BuildGrid.KeepPlaceOnGrid"/>) while the setting is on.
    /// </summary>
    public (float? X, float? Z) KeepPlaceOnGrid(MapDocument document, IEnumerable<ObjectRef> things, float? x, float? z) =>
        Handles.KeepOnGrid ? BuildGrid.KeepPlaceOnGrid(document, things, x, z, UnitKinds.StructureFootprint(Assets.Catalog)) : (x, z);
}
