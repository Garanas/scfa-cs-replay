using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// The ways the place tool places many units at once (user story U-7).
/// </summary>
public enum PlaceMode
{
    /// <summary>
    /// One unit per click, as before.
    /// </summary>
    Single,

    /// <summary>
    /// A drag places a line of units from the press to the mouse.
    /// </summary>
    Line,

    /// <summary>
    /// A drag fills the rectangle between the press and the mouse, or only its border.
    /// </summary>
    Rectangle,

    /// <summary>
    /// A click places units around the selection's footprints, or around the structure under the mouse.
    /// </summary>
    Ring,

    /// <summary>
    /// A brush stroke scatters units at random within its circle.
    /// </summary>
    Scatter,
}

/// <summary>
/// The settings of a layout the place tool and <c>units.addMany</c> share: the places come from the
/// library's <see cref="UnitLayouts"/>, the footprint from the unit's catalogue entry.
/// </summary>
/// <param name="Spacing">Between mobile units, in ogrids.</param>
/// <param name="FaceAlong">Whether a line's units face along it.</param>
/// <param name="Hollow">Whether only a rectangle's border is filled.</param>
/// <param name="Distance">Between a ring and what it goes around, in ogrids.</param>
/// <param name="Heading">The heading of every unit otherwise, in radians.</param>
public readonly record struct UnitLayoutSettings(float Spacing, bool FaceAlong, bool Hollow, float Distance, float Heading);

/// <summary>
/// What a place tool's gesture will put where: the places of a line, rectangle or ring for a unit,
/// and the footprint bounds a ring goes around.
/// </summary>
public static class UnitPlans
{
    /// <summary>
    /// The places of a line from <paramref name="a"/> to <paramref name="b"/>, a rectangle between
    /// them, or a ring around <paramref name="bounds"/>; empty for the other modes.
    /// </summary>
    /// <param name="entry">The unit's catalogue entry (its footprint, whether it is a structure); null for a unit the workspace lacks (1 by 1, mobile).</param>
    public static ImmutableArray<UnitPlace> Of(PlaceMode mode, AssetEntry? entry, Vector2 a, Vector2 b, (float X0, float Z0, float X1, float Z1)? bounds, UnitLayoutSettings settings)
    {
        (double SizeX, double SizeZ)? footprint = entry?.Footprint;
        bool structure = entry?.IsStructure == true;
        return mode switch
        {
            PlaceMode.Line => UnitLayouts.Line(a, b, footprint, settings.Spacing, settings.FaceAlong, settings.Heading, structure),
            PlaceMode.Rectangle => UnitLayouts.Rectangle(a, b, footprint, settings.Hollow, settings.Spacing, settings.Heading, structure),
            PlaceMode.Ring when bounds is { } around => UnitLayouts.Ring(around, footprint, settings.Distance, settings.Spacing, settings.Heading, structure),
            _ => [],
        };
    }

    /// <summary>
    /// The place a single click puts the unit: a structure on the build grid in quarter turns, a
    /// mobile unit where the mouse is.
    /// </summary>
    public static UnitPlace Single(AssetEntry? entry, float x, float z, float heading)
    {
        if (entry is { IsStructure: true })
        {
            float turn = BuildGrid.SnapHeading(heading);
            (double sizeX, double sizeZ) = entry.Footprint ?? (1, 1);
            (float sx, float sz) = BuildGrid.Snap(x, z, sizeX, sizeZ, turn);
            return new UnitPlace(sx, sz, turn);
        }
        return new UnitPlace(x, z, heading);
    }

    /// <summary>
    /// The rectangle the footprints of the selected units cover, else that of the structure whose
    /// footprint holds <paramref name="at"/> (the nearest centre when several do); null without either.
    /// </summary>
    public static (float X0, float Z0, float X1, float Z1)? RingBounds(MapDocument document, AssetCatalog catalog, IReadOnlyList<ObjectRef> selection, Vector2? at)
    {
        (float X0, float Z0, float X1, float Z1)? bounds = null;
        foreach (ObjectRef thing in selection)
        {
            if (thing.Kind == ObjectKind.Unit && document.Find(thing) is PlacedUnit placed)
            {
                bounds = Join(bounds, FootprintBounds(catalog, placed.Unit));
            }
        }
        if (bounds is not null || at is not Vector2 point)
        {
            return bounds;
        }
        float best = float.MaxValue;
        foreach (PlacedUnit placed in document.AllUnits())
        {
            if (catalog.Find(placed.Unit.Blueprint) is not { IsStructure: true })
            {
                continue;
            }
            (float x0, float z0, float x1, float z1) = FootprintBounds(catalog, placed.Unit);
            if (point.X < x0 || point.X > x1 || point.Y < z0 || point.Y > z1)
            {
                continue;
            }
            float distance = Vector2.DistanceSquared(point, new Vector2(placed.Unit.Position.X, placed.Unit.Position.Z));
            if (distance < best)
            {
                best = distance;
                bounds = (x0, z0, x1, z1);
            }
        }
        return bounds;
    }

    /// <summary>
    /// The rectangle a unit's footprint covers, turned in quarter turns about its centre.
    /// </summary>
    public static (float X0, float Z0, float X1, float Z1) FootprintBounds(AssetCatalog catalog, Unit unit)
    {
        (float extentX, float extentZ) = UnitLayouts.Extent(catalog.Find(unit.Blueprint)?.Footprint, unit.Orientation.Y);
        float x = unit.Position.X;
        float z = unit.Position.Z;
        return (x - extentX / 2, z - extentZ / 2, x + extentX / 2, z + extentZ / 2);
    }

    private static (float X0, float Z0, float X1, float Z1) Join((float X0, float Z0, float X1, float Z1)? a, (float X0, float Z0, float X1, float Z1) b) =>
        a is { } first ? (MathF.Min(first.X0, b.X0), MathF.Min(first.Z0, b.Z0), MathF.Max(first.X1, b.X1), MathF.Max(first.Z1, b.Z1)) : b;
}
