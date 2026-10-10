using System.Collections.Immutable;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Symmetry applied to edits (user stories F-12, S-1): the parts of the map an edit is mirrored
/// into, the mirror partners that move, turn and are deleted with the selection, the images of what
/// is placed, and the check of things without a partner. The maths is the library's
/// (<see cref="SymmetricDabs"/>, <see cref="SymmetryEdits"/>, <see cref="SymmetryChecks"/>).
/// </summary>
public sealed partial class MainViewModel
{
    private void InitializeSymmetry()
    {
        Problems.Checks.Register(SymmetryChecks.Check(document => Symmetry.Transforms(document)));
        Symmetry.Changed += (_, _) =>
        {
            if (Map is not null)
            {
                Problems.Schedule();
            }
        };
    }

    /// <summary>
    /// The parts of the map an edit of <paramref name="document"/> is mirrored into, the identity
    /// first: every part while the symmetry mirrors edits, else the identity alone.
    /// </summary>
    public IReadOnlyList<MapSymmetryTransform> EditTransforms(MapDocument document) => Symmetry.EditTransforms(document);

    /// <summary>
    /// The mirror partners of the things that go with them when they move, turn or are deleted:
    /// none while the symmetry does not mirror edits.
    /// </summary>
    public ImmutableArray<SymmetryPartner> PartnersOf(MapDocument document, IEnumerable<ObjectRef> things)
    {
        if (!Symmetry.IsMirroring)
        {
            return [];
        }
        using Performance.Span measure = Performance.Measure("model.symmetry-partners");
        return document.Partners(things, Symmetry.Transforms(document));
    }

    /// <summary>
    /// Adds the images of things just placed in every other part of the map while the symmetry
    /// mirrors edits (<see cref="SymmetryEdits.MirrorObjects"/>): structures snapped to the build grid.
    /// </summary>
    /// <param name="placed">What was placed: the things first selected, with their images after them.</param>
    public MapDocument MirrorPlaced(MapDocument document, IReadOnlyList<ObjectRef> placed, out ImmutableArray<ObjectRef> selected)
    {
        if (!Symmetry.IsMirroring)
        {
            selected = [.. placed];
            return document;
        }
        document = MirrorAll(document, placed, Symmetry.Transforms(document), out ImmutableArray<ObjectRef> images);
        selected = [.. placed, .. images];
        return document;
    }

    /// <summary>
    /// Adds the images of the things in the other parts of the map (<see cref="SymmetryEdits.MirrorObjects"/>),
    /// a structure snapped to the build grid and to quarter turns.
    /// </summary>
    public MapDocument MirrorAll(MapDocument document, IEnumerable<ObjectRef> things, IReadOnlyList<MapSymmetryTransform> transforms, out ImmutableArray<ObjectRef> images)
    {
        using Performance.Span measure = Performance.Measure("model.mirror-objects");
        document = document.MirrorObjects(things, transforms, out images, adjustUnit: SnapStructure);
        Logger("Editor").LogDebug("Mirrored by the symmetry: {Count} images added", images.Length);
        return document;
    }

    /// <summary>
    /// A structure on the build grid and turned in quarter turns, as a structure placed by hand; a
    /// mobile unit as it is.
    /// </summary>
    private Unit SnapStructure(Unit unit)
    {
        if (Assets.Catalog.Find(unit.Blueprint) is not { IsStructure: true } entry)
        {
            return unit;
        }
        float heading = BuildGrid.SnapHeading(unit.Orientation.Y);
        (double sizeX, double sizeZ) = entry.Footprint ?? (1, 1);
        (float x, float z) = BuildGrid.Snap(unit.Position.X, unit.Position.Z, sizeX, sizeZ, heading);
        return unit with
        {
            Position = new System.Numerics.Vector3(x, unit.Position.Y, z),
            Orientation = unit.Orientation with { Y = heading },
        };
    }
}
