using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// The outline of one thing of a preset, relative to the preset's middle in ogrids: a rectangle
/// about <paramref name="Centre"/> reaching <paramref name="HalfU"/> and <paramref name="HalfV"/>
/// from it (half its two sides, turned as the thing lies).
/// </summary>
public readonly record struct PreviewOutline(Vector2 Centre, Vector2 HalfU, Vector2 HalfV)
{
    /// <summary>
    /// A square of <paramref name="half"/> ogrids from its centre to a side.
    /// </summary>
    public static PreviewOutline Square(float x, float z, float half) => new PreviewOutline(new Vector2(x, z), new Vector2(half, 0), new Vector2(0, half));
}

/// <summary>
/// What a preset places, drawn under the mouse before the click (user stories F-10 and U-4): an
/// outline per thing on the ground where a click puts it (its middle on a whole ogrid, turned as
/// the tool turns it), with its images in the other parts of the map while the symmetry mirrors
/// edits. The outlines are made once per preset (<see cref="OfSelection"/>, <see cref="OfUnits"/>);
/// drawing them is span <c>tool.preset-preview</c>.
/// </summary>
public static class PresetPreview
{
    /// <summary>
    /// How many outlines are drawn at most, so a preset of thousands of props stays quick.
    /// </summary>
    public const int MaxOutlines = 2000;

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
    /// The footprints of a units preset's units.
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
            outlines.Add(Footprint(catalog.Find(unit.Blueprint)?.Footprint, unit.Position.X, unit.Position.Z, unit.Orientation.Y));
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
            placed.Add(new PreviewOutline(middle + Turn(outline.Centre), Turn(outline.HalfU), Turn(outline.HalfV)));
        }
        return ShapesAt(model, view, placed);
    }

    /// <summary>
    /// Outlines at their own places on the map (the footprints a drag of the place tool will fill,
    /// user story U-7), in the view's pixels, each with its images in the other parts of the map while
    /// the symmetry mirrors edits: the thing itself with a shadow, an image dashed-light.
    /// </summary>
    /// <param name="span">The performance span the drawing is measured as.</param>
    public static IReadOnlyList<ToolShape> ShapesAt(MainViewModel model, ToolView view, IReadOnlyList<PreviewOutline> outlines, string span = "tool.preset-preview")
    {
        if (outlines.Count == 0 || model.ShownDocument is not MapDocument document)
        {
            return [];
        }
        using Performance.Span measure = Performance.Measure(span);
        Heightmap heights = document.Terrain.Heightmap;
        IReadOnlyList<MapSymmetryTransform> parts = model.EditTransforms(document);
        List<ToolShape> shapes = [];
        Vector2[] corners = new Vector2[4];
        int drawn = 0;
        foreach (PreviewOutline outline in outlines)
        {
            if (drawn++ >= MaxOutlines)
            {
                break;
            }
            Vector2 centre = outline.Centre;
            Vector2 u = outline.HalfU;
            Vector2 v = outline.HalfV;
            corners[0] = centre - u - v;
            corners[1] = centre + u - v;
            corners[2] = centre + u + v;
            corners[3] = centre - u + v;
            foreach (MapSymmetryTransform part in parts)
            {
                Vector2[]? points = new Vector2[4];
                for (int i = 0; i < 4 && points is not null; i++)
                {
                    Vector2 at = part.Apply(corners[i]);
                    Vector3 ground = new Vector3(at.X, heights.HeightAt(Math.Clamp(at.X, 0, document.Width), Math.Clamp(at.Y, 0, document.Height)), at.Y);
                    if (view.ToScreen(ground) is Vector2 screen)
                    {
                        points[i] = screen;
                    }
                    else
                    {
                        points = null;
                    }
                }
                if (points is null)
                {
                    continue;
                }
                bool image = part != MapSymmetryTransform.Identity;
                if (!image)
                {
                    shapes.Add(new ToolLine(points, true, 3, ToolInk.Shadow));
                }
                shapes.Add(new ToolLine(points, true, 1, image ? ToolInk.Hover : ToolInk.Selection));
            }
        }
        return shapes;
    }
}
