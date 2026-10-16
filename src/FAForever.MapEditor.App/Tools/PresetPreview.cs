using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// A rectangle on the ground in ogrids: about <paramref name="Centre"/>, reaching <paramref name="HalfU"/>
/// and <paramref name="HalfV"/> from it (half its two sides, turned as it lies).
/// </summary>
public readonly record struct PreviewRect(Vector2 Centre, Vector2 HalfU, Vector2 HalfV);

/// <summary>
/// The outline of one thing of a preset, relative to the preset's middle in ogrids: a rectangle
/// about <paramref name="Centre"/> reaching <paramref name="HalfU"/> and <paramref name="HalfV"/>
/// from it (half its two sides, turned as the thing lies).
/// </summary>
public readonly record struct PreviewOutline(Vector2 Centre, Vector2 HalfU, Vector2 HalfV)
{
    /// <summary>
    /// The ground a structure reserves around its footprint, its skirt (user story U-12), drawn
    /// dashed; null when the skirt is the footprint itself or the thing is no structure.
    /// </summary>
    public PreviewRect? Skirt { get; init; }

    /// <summary>
    /// A square of <paramref name="half"/> ogrids from its centre to a side.
    /// </summary>
    public static PreviewOutline Square(float x, float z, float half) => new PreviewOutline(new Vector2(x, z), new Vector2(half, 0), new Vector2(0, half));
}

/// <summary>
/// What a preset places, drawn under the mouse before the click (user stories F-10 and U-4): an
/// outline per thing on the ground where a click puts it (its middle on a whole ogrid, turned as
/// the tool turns it), with its images in the other parts of the map while the symmetry mirrors
/// edits; a structure's skirt dashed around its footprint (U-12). The outlines are made once per
/// preset (<see cref="OfSelection"/>, <see cref="OfUnits"/>); drawing them is span <c>tool.preset-preview</c>.
/// </summary>
public static class PresetPreview
{
    /// <summary>
    /// How many outlines are drawn at most, so a preset of thousands of props stays quick.
    /// </summary>
    public const int MaxOutlines = 2000;

    /// <summary>
    /// The width of the outline of a unit that breaks a rule, and of the dark halo under it, in pixels.
    /// </summary>
    private const float BreakingWidth = 3;

    private const float BreakingHalo = 7;

    /// <summary>
    /// The outlines of a selection preset's things: props and markers as small squares, decals as
    /// their rectangles, units as their footprints.
    /// </summary>
    public static IReadOnlyList<PreviewOutline> OfSelection(SelectionPresetSettings settings, AssetCatalog catalog)
    {
        List<PreviewOutline> outlines = [];
        (IReadOnlyList<Prop> props, IReadOnlyList<Decal> decals, MarkerSet? markers, UnitGroup? units) = settings.ToModel();
        foreach (Prop prop in props)
        {
            outlines.Add(PreviewOutline.Square(prop.Position.X, prop.Position.Z, 0.5f));
        }
        foreach (Decal decal in decals)
        {
            (Vector2 u, Vector2 v) = MapObjects.DecalEdges(decal.Scale, decal.Rotation.Y);
            Vector3 centre = MapObjects.DecalCentre(decal);
            outlines.Add(new PreviewOutline(new Vector2(centre.X, centre.Z), u / 2, v / 2));
        }
        if (markers is not null)
        {
            foreach (Marker marker in markers.All)
            {
                float half = marker is ResourceMarker resource ? MarkerEdits.DepositSize(resource.Kind) / 2 : 1;
                outlines.Add(PreviewOutline.Square(marker.Position.X, marker.Position.Z, half));
            }
        }
        if (units is not null)
        {
            AddUnits(outlines, units, catalog);
        }
        return outlines;
    }

    /// <summary>
    /// The footprints of a units preset's units, with their skirts.
    /// </summary>
    public static IReadOnlyList<PreviewOutline> OfUnits(UnitGroup group, AssetCatalog catalog)
    {
        List<PreviewOutline> outlines = [];
        AddUnits(outlines, group, catalog);
        return outlines;
    }

    private static void AddUnits(List<PreviewOutline> outlines, UnitGroup group, AssetCatalog catalog)
    {
        foreach (Unit unit in group.Units)
        {
            outlines.Add(OfUnit(catalog.Find(unit.Blueprint), unit.Position.X, unit.Position.Z, unit.Orientation.Y));
        }
        foreach (UnitGroup child in group.Groups)
        {
            AddUnits(outlines, child, catalog);
        }
    }

    /// <summary>
    /// The footprint of one unit where it would stand: its size turned by its heading, about its centre.
    /// </summary>
    /// <param name="radians">The unit's heading, clockwise in the top view.</param>
    public static PreviewOutline Footprint((double SizeX, double SizeZ)? footprint, float x, float z, float radians)
    {
        (double sizeX, double sizeZ) = footprint ?? (1, 1);
        (float sin, float cos) = MathF.SinCos(radians);
        float hx = (float)sizeX / 2;
        float hz = (float)sizeZ / 2;
        return new PreviewOutline(new Vector2(x, z), new Vector2(hx * cos, hx * sin), new Vector2(-hz * sin, hz * cos));
    }

    /// <summary>
    /// The footprint of a unit where it would stand (<see cref="Footprint"/>) and, for a structure
    /// whose skirt reaches beyond its footprint, the skirt around it (user story U-12: the ground the
    /// game reserves, <see cref="PlacementChecks.SkirtOf"/>).
    /// </summary>
    /// <param name="entry">The unit; null for one the workspace lacks, placed 1 by 1.</param>
    public static PreviewOutline OfUnit(AssetEntry? entry, float x, float z, float radians)
    {
        PreviewOutline footprint = Footprint(entry?.Footprint, x, z, radians);
        if (entry is not { IsStructure: true })
        {
            return footprint;
        }
        PlacementRect skirt = PlacementChecks.SkirtOf(entry, x, z, radians);
        return skirt == PlacementChecks.FootprintOf(entry, x, z, radians) ? footprint : footprint with { Skirt = SkirtRect(skirt) };
    }

    /// <summary>
    /// A skirt as a rectangle to draw.
    /// </summary>
    public static PreviewRect SkirtRect(PlacementRect skirt) => new PreviewRect(
        new Vector2((skirt.MinX + skirt.MaxX) / 2, (skirt.MinZ + skirt.MaxZ) / 2),
        new Vector2((skirt.MaxX - skirt.MinX) / 2, 0),
        new Vector2(0, (skirt.MaxZ - skirt.MinZ) / 2));

    /// <summary>
    /// The outlines where a click at the mouse (<see cref="MainViewModel.Pointer"/>) puts them,
    /// in the view's pixels; none while the mouse is outside the views.
    /// </summary>
    /// <param name="radians">The tool's turn, clockwise in the top view.</param>
    public static IReadOnlyList<ToolShape> Shapes(MainViewModel model, ToolView view, IReadOnlyList<PreviewOutline> outlines, float radians)
    {
        if (outlines.Count == 0 || model.Pointer is not (float px, float pz))
        {
            return [];
        }
        Vector2 middle = new Vector2(MathF.Round(px), MathF.Round(pz));
        (float sin, float cos) = MathF.SinCos(radians);
        Vector2 Turn(Vector2 v) => new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        List<PreviewOutline> placed = new List<PreviewOutline>(Math.Min(outlines.Count, MaxOutlines));
        foreach (PreviewOutline outline in outlines)
        {
            if (placed.Count >= MaxOutlines)
            {
                break;
            }
            PreviewOutline turned = new PreviewOutline(middle + Turn(outline.Centre), Turn(outline.HalfU), Turn(outline.HalfV));
            if (outline.Skirt is PreviewRect skirt)
            {
                turned = turned with { Skirt = new PreviewRect(middle + Turn(skirt.Centre), Turn(skirt.HalfU), Turn(skirt.HalfV)) };
            }
            placed.Add(turned);
        }
        return ShapesAt(model, view, placed);
    }

    /// <summary>
    /// Outlines at their own places on the map (the footprints a drag of the place tool will fill,
    /// user story U-7), in the view's pixels, each with its images in the other parts of the map while
    /// the symmetry mirrors edits: the thing itself with a shadow, an image dashed-light; a skirt
    /// dashed in the same ink, without a shadow.
    /// </summary>
    /// <param name="span">The performance span the drawing is measured as.</param>
    /// <param name="blocked">Per outline whether it breaks a rule (user story U-12): drawn in the danger colour.</param>
    public static IReadOnlyList<ToolShape> ShapesAt(MainViewModel model, ToolView view, IReadOnlyList<PreviewOutline> outlines, string span = "tool.preset-preview", IReadOnlyList<bool>? blocked = null)
    {
        if (outlines.Count == 0 || model.ShownDocument is not MapDocument document)
        {
            return [];
        }
        using Performance.Span measure = Performance.Measure(span);
        Heightmap heights = document.Terrain.Heightmap;
        IReadOnlyList<MapSymmetryTransform> parts = model.EditTransforms(document);
        List<ToolShape> shapes = [];
        // the corners of a rectangle on the ground in a part of the map, in the view's pixels; null when one lies behind the camera
        Vector2[]? OnScreen(Vector2 centre, Vector2 u, Vector2 v, MapSymmetryTransform part)
        {
            ReadOnlySpan<Vector2> corners = [centre - u - v, centre + u - v, centre + u + v, centre - u + v];
            Vector2[] points = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                Vector2 at = part.Apply(corners[i]);
                Vector3 ground = new Vector3(at.X, heights.HeightAt(Math.Clamp(at.X, 0, document.Width), Math.Clamp(at.Y, 0, document.Height)), at.Y);
                if (view.ToScreen(ground) is not Vector2 screen)
                {
                    return null;
                }
                points[i] = screen;
            }
            return points;
        }
        int drawn = 0;
        foreach (PreviewOutline outline in outlines)
        {
            int number = drawn++;
            if (number >= MaxOutlines)
            {
                break;
            }
            foreach (MapSymmetryTransform part in parts)
            {
                if (OnScreen(outline.Centre, outline.HalfU, outline.HalfV, part) is not Vector2[] points)
                {
                    continue;
                }
                bool image = part != MapSymmetryTransform.Identity;
                bool breaks = !image && blocked is not null && number < blocked.Count && blocked[number];
                ToolInk ink = breaks ? ToolInk.Danger : image ? ToolInk.Hover : ToolInk.Selection;
                if (!image)
                {
                    // a rule-breaking outline is thicker and has a wide dark halo, so it reads on water and on bright ground
                    shapes.Add(new ToolLine(points, true, breaks ? BreakingHalo : 3, ToolInk.Shadow));
                }
                shapes.Add(new ToolLine(points, true, breaks ? BreakingWidth : 1, ink));
                if (outline.Skirt is PreviewRect skirt && OnScreen(skirt.Centre, skirt.HalfU, skirt.HalfV, part) is Vector2[] reserved)
                {
                    // the skirt dashed, so the reserved ground is told from the footprint (no halo: its dashes would not line up)
                    shapes.Add(new ToolLine(reserved, true, breaks ? 2 : 1, ink) { Dash = ToolDash.Dashed });
                }
            }
        }
        return shapes;
    }
}
