using Avalonia;
using Avalonia.Media;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The units in the top view (user story U-10): a structure as its footprint, turned by its heading,
/// outlined and filled faintly in its army's colour; a mobile unit as a small diamond in that colour;
/// a wreck as an ashen cross. Zoomed so far out that a footprint is under a few pixels, a structure
/// is the small square again. With the overlay <c>armyColours</c> off every unit but a wreck is the
/// square of the theme's unit colour.
/// </summary>
public sealed partial class HeightmapView
{
    // a footprint smaller than this on screen is drawn as the small square
    private const double FootprintMinimumPixels = 3;

    private sealed record ArmyInk(IBrush Solid, IBrush Faint, IPen Outline);

    private readonly Dictionary<uint, ArmyInk> _armyInks = [];

    /// <summary>
    /// How many units the top view drew the last time as footprints, diamonds, squares and crosses (automation).
    /// </summary>
    public (int Footprints, int Diamonds, int Squares, int Wrecks) UnitsDrawn { get; private set; }

    private ArmyInk InkOf(uint argb)
    {
        if (!_armyInks.TryGetValue(argb, out ArmyInk? ink))
        {
            Color colour = Color.FromUInt32(argb);
            ink = new ArmyInk(new SolidColorBrush(colour), new SolidColorBrush(colour, 0.3), new Pen(new SolidColorBrush(colour), 1.5));
            _armyInks[argb] = ink;
        }
        return ink;
    }

    private void DrawUnits(DrawingContext context, Overlay overlay, MapDocument document, MainViewModel model, Rect shown)
    {
        using Performance.Span measure = Performance.Measure("top.units");
        int footprints = 0, diamonds = 0, squares = 0, wrecks = 0;
        bool colours = model.ShowArmyColours;
        AssetCatalog catalog = model.Assets.Catalog;
        foreach (PlacedUnit unit in document.AllUnits())
        {
            Point at = MapToControl(unit.Unit.Position.X, unit.Unit.Position.Z);
            AssetEntry? entry = catalog.Find(unit.Unit.Blueprint);
            (double SizeX, double SizeZ)? footprint = entry is { IsStructure: true } ? entry.Footprint ?? (1, 1) : null;
            // a footprint reaches beyond the centre: what is left of the view still shows
            double reach = footprint is (double sizeX, double sizeZ) ? Math.Max(sizeX, sizeZ) * _zoom : 0;
            if (!shown.Inflate(reach).Contains(at) || !model.IsDrawn(ObjectRef.Unit(unit.Unit.Name)))
            {
                continue;
            }
            if (UnitWrecks.IsWreck(unit))
            {
                // a wreck (U-5): an ashen cross where a unit is a square
                foreach (IPen pen in (ReadOnlySpan<IPen>)[overlay.WreckShadow, overlay.Wreck])
                {
                    context.DrawLine(pen, at + new Vector(-3.5, -3.5), at + new Vector(3.5, 3.5));
                    context.DrawLine(pen, at + new Vector(-3.5, 3.5), at + new Vector(3.5, -3.5));
                }
                wrecks++;
                continue;
            }
            ArmyInk? ink = colours ? InkOf(ArmyColours.Of(document, unit.Army)) : null;
            if (colours && footprint is (double sx, double sz) && Math.Min(Math.Max(1, sx), Math.Max(1, sz)) * _zoom >= FootprintMinimumPixels)
            {
                Avalonia.Media.StreamGeometry outline = new StreamGeometry();
                System.Numerics.Vector2[] corners = MapObjects.FootprintCorners(unit.Unit, sx, sz);
                using (StreamGeometryContext geometry = outline.Open())
                {
                    geometry.BeginFigure(MapToControl(corners[0].X, corners[0].Y), true);
                    for (int i = 1; i < corners.Length; i++)
                    {
                        geometry.LineTo(MapToControl(corners[i].X, corners[i].Y));
                    }
                    geometry.EndFigure(true);
                }
                context.DrawGeometry(ink!.Faint, ink.Outline, outline);
                footprints++;
            }
            else if (colours && footprint is null)
            {
                // a mobile unit: a small diamond
                StreamGeometry diamond = new StreamGeometry();
                using (StreamGeometryContext geometry = diamond.Open())
                {
                    geometry.BeginFigure(new Point(at.X, at.Y - 4.5), true);
                    geometry.LineTo(new Point(at.X + 4.5, at.Y));
                    geometry.LineTo(new Point(at.X, at.Y + 4.5));
                    geometry.LineTo(new Point(at.X - 4.5, at.Y));
                    geometry.EndFigure(true);
                }
                context.DrawGeometry(ink!.Solid, overlay.Shadow, diamond);
                diamonds++;
            }
            else
            {
                context.DrawRectangle(ink?.Solid ?? overlay.Unit, overlay.Shadow, new Rect(at.X - 3.5, at.Y - 3.5, 7, 7));
                squares++;
            }
        }
        UnitsDrawn = (footprints, diamonds, squares, wrecks);
    }
}
