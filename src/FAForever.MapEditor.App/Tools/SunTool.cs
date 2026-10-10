using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Turns the sun with the mouse (user story L-1), in either view: the sun stands where the mouse
/// points, seen from the middle of the map. The direction from the middle to the mouse is the
/// azimuth; the distance is the elevation, straight up at the middle and low over the horizon at
/// half the map's size from it. A drag is one step of undo, and the 3D view is lit by it while
/// the mouse moves. The top view draws the line from the middle to the sun; the 3D view an arrow
/// from the middle of the map towards the sun, whose head can be dragged across the sky: the sun
/// then stands where the mouse points on the sphere the head moves on.
/// </summary>
public sealed partial class SunTool(MainViewModel model) : ObservableObject, IMapTool
{
    /// <summary>
    /// The lowest the tool puts the sun, in degrees: lower, the terrain would be all shadow.
    /// </summary>
    public const double LowestElevation = 5;

    /// <summary>
    /// How far, in pixels, the mouse may be from the arrow's head in 3D to take it.
    /// </summary>
    private const float HeadPixels = 10;

    private EditTransaction? _turn;
    private MapDocument? _before;
    private bool _head;
    private ToolView? _view;

    /// <summary>
    /// Whether the mouse is over the arrow's head in 3D (when the view last drew) or drags it.
    /// </summary>
    public bool HeadHovered { get; private set; }

    /// <summary>
    /// The 3D view's arrow: from the middle of the map on the terrain towards the sun, a quarter of
    /// the map's size long.
    /// </summary>
    public static (Vector3 From, Vector3 To) Arrow(MapDocument document)
    {
        float x = document.Width / 2f;
        float z = document.Height / 2f;
        Vector3 from = new Vector3(x, document.Terrain.Heightmap.HeightAt(x, z), z);
        Vector3 sun = document.Lighting.SunDirection.LengthSquared() > 0 ? Vector3.Normalize(document.Lighting.SunDirection) : Vector3.UnitY;
        return (from, from + sun * ArrowLength(document));
    }

    private static float ArrowLength(MapDocument document) => Math.Min(document.Width, document.Height) / 4f;

    /// <summary>
    /// The sun's direction for a ray: towards where it meets the sphere the arrow's head moves on
    /// (or the point of the ray nearest the sphere's middle when it misses), never lower than
    /// <see cref="LowestElevation"/>.
    /// </summary>
    public static Vector3 DirectionAt(MapDocument document, Vector3 origin, Vector3 direction)
    {
        (Vector3 centre, _) = Arrow(document);
        float radius = ArrowLength(document);
        Vector3 d = Vector3.Normalize(direction);
        Vector3 oc = origin - centre;
        float b = Vector3.Dot(oc, d);
        float c = oc.LengthSquared() - radius * radius;
        float discriminant = b * b - c;
        float t = discriminant >= 0 ? -b - MathF.Sqrt(discriminant) : MathF.Max(-b, 0);
        if (t < 0)
        {
            t = -b + MathF.Sqrt(MathF.Max(discriminant, 0));
        }
        Vector3 towards = origin + d * t - centre;
        if (towards.LengthSquared() < 1e-9f)
        {
            return Vector3.UnitY;
        }
        (double azimuth, double elevation) = SunAngles.Of(Vector3.Normalize(towards));
        return SunAngles.Direction(azimuth, Math.Clamp(elevation, LowestElevation, 90));
    }

    public IReadOnlyList<ToolShape> Shapes(ToolView view)
    {
        if (view.IsTopDown || model.ShownDocument is not MapDocument document)
        {
            return [];
        }
        (Vector3 from, Vector3 to) = Arrow(document);
        if (view.ToScreen(from) is not Vector2 start || view.ToScreen(to) is not Vector2 head)
        {
            return [];
        }
        HeadHovered = _turn is not null ? _head : view.Mouse is Vector2 mouse && Vector2.Distance(mouse, head) <= HeadPixels;
        return
        [
            new ToolLine([start, head], false, 5, ToolInk.Shadow),
            new ToolLine([start, head], false, 3, ToolInk.Sun),
            new ToolSquare(head, 6, HeadHovered ? ToolInk.Hover : ToolInk.Sun, ToolInk.Shadow),
        ];
    }

    public void Press(float x, float z, ToolInput input)
    {
        _head = false;
        _view = null;
        if (input.View is { IsTopDown: false, Mouse: Vector2 mouse } view && model.ShownDocument is MapDocument document
            && view.ToScreen(Arrow(document).To) is Vector2 head && Vector2.Distance(mouse, head) <= HeadPixels)
        {
            _head = true;
            _view = view;
        }
        Press(x, z);
    }

    public void Drag(float x, float z, ToolInput input)
    {
        _view = input.View ?? _view;
        Drag(x, z);
    }

    public double CursorRadius => 0;

    public string Help => "Drag in a view: the sun stands where the mouse points, seen from the middle of the map; at the middle straight up, towards the edge low";

    /// <summary>
    /// The sun as the tool last put it, e.g. <c>Azimuth 135°, elevation 40°</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Angles { get; private set; } = "";

    public ToolMarks? Marks
    {
        get
        {
            if (model.ShownDocument is not MapDocument document)
            {
                return null;
            }
            (double azimuth, double elevation) = SunAngles.Of(document.Lighting.SunDirection);
            (float x, float z) = PlaceOf(document, azimuth, elevation);
            return new ToolMarks([(x, z)], (document.Width / 2f, document.Height / 2f, x, z), Describe(azimuth, elevation));
        }
    }

    /// <summary>
    /// The sun's angles for a place of the map, in degrees.
    /// </summary>
    public static (double Azimuth, double Elevation) AnglesAt(MapDocument document, float x, float z)
    {
        double dx = x - document.Width / 2.0;
        double dz = z - document.Height / 2.0;
        double reach = Math.Min(document.Width, document.Height) / 2.0;
        double azimuth = Math.Atan2(dx, -dz) * 180 / Math.PI;
        double elevation = Math.Clamp(90 * (1 - Math.Sqrt(dx * dx + dz * dz) / reach), LowestElevation, 90);
        return (azimuth < 0 ? azimuth + 360 : azimuth, elevation);
    }

    /// <summary>
    /// The place of the map that stands for the sun's angles: the reverse of <see cref="AnglesAt"/>.
    /// </summary>
    public static (float X, float Z) PlaceOf(MapDocument document, double azimuth, double elevation)
    {
        double reach = Math.Min(document.Width, document.Height) / 2.0 * (1 - Math.Clamp(elevation, 0, 90) / 90);
        double a = azimuth * Math.PI / 180;
        return ((float)(document.Width / 2.0 + reach * Math.Sin(a)), (float)(document.Height / 2.0 - reach * Math.Cos(a)));
    }

    public static string Describe(double azimuth, double elevation) =>
        string.Create(CultureInfo.InvariantCulture, $"Sun: azimuth {azimuth:0}°, elevation {elevation:0}°");

    public void Press(float x, float z)
    {
        if (model.ShownDocument is null || model.StrokeInProgress is not null)
        {
            return;
        }
        _turn = model.StartStroke("Turn the sun");
        _before = _turn.Document;
        Turn(x, z);
    }

    public void Drag(float x, float z)
    {
        if (_turn is not null)
        {
            Turn(x, z);
        }
    }

    public void Release()
    {
        if (_turn is not null)
        {
            model.EndStroke(_turn);
            model.Status = Angles;
        }
        _turn = null;
        _before = null;
    }

    private void Turn(float x, float z)
    {
        if (model.StrokeInProgress != _turn || _before is not MapDocument before)
        {
            return;
        }
        Vector3 direction;
        double azimuth;
        double elevation;
        if (_head && _view?.Ray is (Vector3 origin, Vector3 ray))
        {
            direction = DirectionAt(before, origin, ray);
            (azimuth, elevation) = SunAngles.Of(direction);
        }
        else
        {
            (azimuth, elevation) = AnglesAt(before, x, z);
            direction = SunAngles.Direction(azimuth, elevation);
        }
        _turn!.Apply(_ => before with { Lighting = before.Lighting with { SunDirection = direction } });
        Angles = Describe(azimuth, elevation);
        model.Status = Angles;
        model.ShowStroke();
    }
}
